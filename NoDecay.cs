using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Facepunch;

using Rust;

using Newtonsoft.Json;

using Oxide.Core;

using ProtoBuf;

using Environment = System.Environment;

namespace Oxide.Plugins
{
    [Info("No Decay", "0x89A", "1.5.0")]
    [Description("Scales or disables decay of items and deployables")]
    class NoDecay : RustPlugin
    {
        private Configuration _config;

        private readonly Dictionary<ulong, BuildingPrivlidge> _cachedToolCupboards = new();

        private const int WoodItemId = -151838493;
        private const int StoneItemId = -2099697608;
        private const int MetalFragmentsItemId = 69511070;
        private const int HqMetalItemId = 317398316;
        
        private readonly uint _toolCupboardPrefabId = StringPool.Get("cupboard.tool.deployed");
        private readonly uint _retroToolCupboardPrefabId = StringPool.Get("cupboard.tool.retro.deployed");
        private readonly uint _shockByteToolCupboardPrefabId = StringPool.Get("cupboard.tool.shockbyte.deployed");

        private readonly ConcurrentQueue<string> _logQueue = new();
        private bool _logQueueRunning;

        private string _logFileName;

        private void Init()
        {
            if (_config.General.Output.logToFile)
            {
                string logDirectory = Path.Combine(Interface.Oxide.LogDirectory, Name);
            
                _logFileName = Path.Combine(logDirectory, $"{_config.General.Output.logFileName}_");
            
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }
            }
            
            permission.RegisterPermission(_config.General.permission, this);
        }

        private void Output(string text)
        {
            if (_config.General.Output.rconOutput)
            {
                Puts(text);
            }
            
            if (_config.General.Output.logToFile)
            {
                if (!text.EndsWith(Environment.NewLine))
                {
                    text += Environment.NewLine;
                }
                
                _logQueue.Enqueue(text);

                if (!_logQueueRunning)
                {
                    _ = Task.Run(LogWorker);
                }
            }
        }

        private void LogWorker()
        {
            _logQueueRunning = true;

            string fileName = _logFileName + $"{DateTime.Now:yyyy-MM-dd}.txt";
            
            while (_logQueue.TryDequeue(out string message))
            {
                File.AppendAllText(fileName, message);
            }

            _logQueueRunning = false;
        }

        #region -Oxide Hooks-
        
        private object OnEntityTakeDamage(DecayEntity entity, HitInfo info)
        {
            if (info?.damageTypes == null || !info.damageTypes.Has(DamageType.Decay) || entity?.net == null)
            {
                return null;
            }

            ulong netId = entity.net.ID.Value;
            if (!_cachedToolCupboards.TryGetValue(netId, out BuildingPrivlidge toolCupboard) || toolCupboard == null)
            {
                toolCupboard = entity.GetBuildingPrivilege();
                _cachedToolCupboards[netId] = toolCupboard;
            }

            if (_config.General.usePermission)
            {
                // Block decaying if the entity has no owner player
                if (entity.OwnerID == 0)
                {
                    return !_config.General.decayNoOwner ? true : null;
                }

                // Block decay if owner has permission 
                if (permission.UserHasPermission(entity.OwnerID.ToString(), _config.General.permission)) 
                {
                    return true; 
                }

                if (_config.General.CupboardSettings.anyAuthed && CheckAllToolCupboardPlayers(toolCupboard))
                {
                    return true;
                }

                Output(string.Format(lang.GetMessage("NoPermission", this), entity.OwnerID));
            }
            
            // Don't block decay if out of range of a tool cupboard
            if (_config.General.CupboardSettings.requireTC && !AnyToolCupboards(entity))
            {
                Output(string.Format(lang.GetMessage("OutOfRange", this), entity.ShortPrefabName, entity.transform.position));
                return null;
            }

            if (_config.General.disableAll)
            {
                return true;
            }
            
            // Scale damage for building blocks
            if (entity is BuildingBlock block)
            {
                info.damageTypes.ScaleAll(_config.buildingMultipliers[(int)block.grade]);

                Output(string.Format(lang.GetMessage("DecayBlocked", this), block.ShortPrefabName, block.transform.position));
                
                // Prevent repair cooldown if no decay damage is done
                if (!info.hasDamage)
                {
                    return true;
                }

                return null;
            }

            string matchingKey = null;
            if (_config.multipliers.ContainsKey(entity.ShortPrefabName) || IsOfType(entity, out matchingKey))
            {
                if (_config.General.excludeOthers)
                {
                    return null;
                }
                
                info.damageTypes.ScaleAll(_config.multipliers[matchingKey ?? entity.ShortPrefabName]);

                Output(string.Format(lang.GetMessage("DecayBlocked", this), entity.ShortPrefabName, entity.transform.position));
                
                // Prevent repair cooldown if no decay damage is done
                if (!info.hasDamage)
                {
                    return true;
                }
            }
            
            return null;
        }

        private object CanMoveItem(Item item, PlayerInventory playerLoot, ItemContainerId targetContainer, int targetSlot, int amount)
        {
            BaseEntity entity = playerLoot.FindContainer(targetContainer)?.entityOwner;
            if (!IsToolCupboard(entity))
            {
                return null;
            }

            bool shouldBlock = item.info.itemid switch
            {
                WoodItemId => _config.General.CupboardSettings.blockWood,
                StoneItemId => _config.General.CupboardSettings.blockStone,
                MetalFragmentsItemId => _config.General.CupboardSettings.blockMetal,
                HqMetalItemId => _config.General.CupboardSettings.blockHighQ,
                _ => false
            };

            if (shouldBlock)
            {
                Output(lang.GetMessage("ItemMoveBlocked", this).Replace("{0}", item.info.shortname).Replace("{1}", $"{entity.ShortPrefabName}"));
                return true;
            }

            return null;
        }

        private bool AnyToolCupboards(BaseEntity entity)
        {
            BuildingManager.Building building = null;

            if (entity is BuildingBlock block)
            {
                building = block.GetBuilding();
            }
            else
            {
                OBB obb = entity.WorldSpaceBounds();

                List<BuildingBlock> list = Pool.Get<List<BuildingBlock>>();
                Vis.Entities(obb.position, 16f + obb.extents.magnitude, list, 2097152);
            
                for (int index = 0; index < list.Count; ++index)
                {
                    BuildingBlock buildingBlock = list[index];
                    if (!(obb.Distance(buildingBlock.WorldSpaceBounds()) <= 16))
                    {
                        continue;
                    }

                    building = buildingBlock.GetBuilding();
                    break;
                }
                
                Pool.FreeUnmanaged(ref list);
            }
            
            if (building != null && building.HasBuildingPrivileges())
            {
                return true;
            }

            return false;
        }

        private bool CheckAllToolCupboardPlayers(BuildingPrivlidge toolCupboard)
        {
            if (toolCupboard == null || !toolCupboard.AnyAuthed())
            {
                return false;
            }

            foreach (PlayerNameID player in toolCupboard.authorizedPlayers)
            {
                if (player == null)
                {
                    continue;
                }
                
                if (permission.UserHasPermission(player.userid.ToString(), _config.General.permission))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsOfType(BaseEntity entity, out string matchingKey)
        {
            matchingKey = null;

            string typeName = entity.GetType().Name;

            foreach (string key in _config.multipliers.Keys)
            {
                if (string.Equals(key, typeName, StringComparison.OrdinalIgnoreCase))
                {
                    matchingKey = key;
                    return true;
                }
            }

            return false;
        }

        private bool IsToolCupboard(BaseEntity entity)
        {
            return entity != null && (entity.prefabID == _toolCupboardPrefabId || entity.prefabID == _retroToolCupboardPrefabId ||
                                      entity.prefabID == _shockByteToolCupboardPrefabId);
        }

        #endregion

        #region -Configuration-

        private class Configuration
        {
            [JsonProperty(PropertyName = "General")]
            public GeneralSettings General = new GeneralSettings();

            [JsonProperty(PropertyName = "Building grade multipliers")]
            public TierMultipliers BuildingTiers = new TierMultipliers();

            [JsonProperty(PropertyName = "Other multipliers")]
            public Dictionary<string, float> multipliers = new Dictionary<string, float>();

            #region -Classes-

            public class GeneralSettings
            {
                [JsonProperty(PropertyName = "Disable decay for all entities")]
                public bool disableAll = false;

                [JsonProperty(PropertyName = "Exclude \"Other Multipliers\"")]
                public bool excludeOthers = false;

                [JsonProperty(PropertyName = "Use permission")]
                public bool usePermission = true;

                [JsonProperty(PropertyName = "Decay if there is no owner (and perms enabled)")]
                public bool decayNoOwner = false;

                [JsonProperty(PropertyName = "Permission")]
                public string permission = "nodecay.use";

                [JsonProperty(PropertyName = "Output")]
                public OutputClass Output = new OutputClass();

                [JsonProperty(PropertyName = "Cupboard Settings")]
                public CupboardSettingsClass CupboardSettings = new CupboardSettingsClass();

                public class OutputClass
                {
                    [JsonProperty(PropertyName = "Output to server console")]
                    public bool rconOutput = false;

                    [JsonProperty(PropertyName = "Log to file")]
                    public bool logToFile = false;

                    [JsonProperty(PropertyName = "Log file name")]
                    public string logFileName = "NoDecay-Log";
                }

                public class CupboardSettingsClass
                {
                    [JsonProperty(PropertyName = "Disable No Decay if resources placed in TC")]
                    public bool disableOnResources = false;

                    [JsonProperty(PropertyName = "Require Tool Cupboard")]
                    public bool requireTC = false;

                    [JsonProperty(PropertyName = "Any authed on TC")]
                    public bool anyAuthed = false;

                    [JsonProperty(PropertyName = "Cupboard Range")]
                    public float cupboardRange = 30f;

                    [JsonProperty(PropertyName = "Block cupboard wood")]
                    public bool blockWood = false;

                    [JsonProperty(PropertyName = "Block cupboard stone")]
                    public bool blockStone = false;

                    [JsonProperty(PropertyName = "Block cupbard metal")]
                    public bool blockMetal = false;

                    [JsonProperty(PropertyName = "Block cupboard high quality")]
                    public bool blockHighQ = false;
                }
            }

            public class TierMultipliers
            {
                [JsonProperty(PropertyName = "Twig multiplier")]
                public float twig = 1f;

                [JsonProperty(PropertyName = "Wood multiplier")]
                public float wood = 0f;

                [JsonProperty(PropertyName = "Stone multiplier")]
                public float stone = 0f;

                [JsonProperty(PropertyName = "Sheet Metal multiplier")]
                public float metal = 0f;

                [JsonProperty(PropertyName = "Armoured multiplier")]
                public float armoured = 0f;
            }

            #endregion -Classes-

            [JsonIgnore]
            public float[] buildingMultipliers;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();
                _config.buildingMultipliers = new float[5] { _config.BuildingTiers.twig, _config.BuildingTiers.wood, _config.BuildingTiers.stone, _config.BuildingTiers.metal, _config.BuildingTiers.armoured };
                SaveConfig();
            }
            catch
            {
                PrintError("Error loading config, using default values");
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig() => _config = new Configuration();

        protected override void SaveConfig() => Config.WriteObject(_config);

        #endregion -Configuration-

        #region -Localisation-

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "{0} does not have permission\n",
                ["ItemMoveBlocked"] = "{0} was blocked from being added to TC at {1}\n",
                ["DecayBlocked"] = "Decay was overriden on {0} at {1}\n",
                ["OutOfRange"] = "{0} was out of TC range, at {1}\n",
            }, this);
        }

        #endregion -Localisation-
    }
}