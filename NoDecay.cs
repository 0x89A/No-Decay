using System;
using System.Collections.Generic;
using Rust;

using UnityEngine;

using Newtonsoft.Json;
using ProtoBuf;

namespace Oxide.Plugins
{
    [Info("No Decay", "0x89A", "1.4.8")]
    [Description("Scales or disables decay of items and deployables")]
    class NoDecay : RustPlugin
    {
        private Configuration _config;

        private readonly Dictionary<BaseCombatEntity, BuildingPrivlidge> _cachedPriviledges = new Dictionary<BaseCombatEntity, BuildingPrivlidge>();

        void Init() => permission.RegisterPermission(_config.General.permission, this);

        void Output(string text)
        {
            if (_config.General.Output.rconOutput) Puts(text);
            if (_config.General.Output.logToFile) LogToFile(_config.General.Output.logFileName, text, this);
        }

        #region -Oxide Hooks-

        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info?.damageTypes == null || entity == null || !info.damageTypes.Has(DamageType.Decay))
            {
                return null;
            }

            //Get tool cupboard
            BuildingPrivlidge priv;
            if (!_cachedPriviledges.TryGetValue(entity, out priv) || priv == null)
            {
                priv = entity.GetBuildingPrivilege();
                _cachedPriviledges[entity] = priv;
            }

            if (_config.General.usePermission && entity.OwnerID != 0 && !permission.UserHasPermission(priv == null ? entity.OwnerID.ToString() : GetOwnerPlayer(priv, entity.OwnerID), _config.General.permission))
            {
                Output(string.Format(lang.GetMessage("NoPermission", this), entity.OwnerID));
                return null;
            }

            //Block decaying if the entity has no owner player
            if (_config.General.usePermission && !_config.General.decayNoOwner && entity.OwnerID == 0)
            {
                return true;
            }

            if (_config.General.usePermission)
            {
                string userId = priv == null ? entity.OwnerID.ToString() : GetOwnerPlayer(priv, entity.OwnerID);
                
                //Continue decaying if player does not have permission
                if (entity.OwnerID != 0 && !permission.UserHasPermission(userId, _config.General.permission))
                {
                    Output(string.Format(lang.GetMessage("NoPermission", this), entity.OwnerID));
                    return null;
                }

                //Block decaying if the entity has no owner player
                if (!_config.General.decayNoOwner && entity.OwnerID == 0)
                {
                    return true;
                }
            }

            //Don't scale damage if out of range of a tool cupboard
            if (_config.General.CupboardSettings.requireTC && !AnyToolCupboards(entity))
            {
                Output(string.Format(lang.GetMessage("OutOfRange", this), entity.ShortPrefabName, entity.transform.position));
                return null;
            }

            //Scale damage for building blocks
            BuildingBlock block = entity as BuildingBlock;
            if (block != null)
            {
                info.damageTypes.ScaleAll(_config.buildingMultipliers[(int)block.grade]);

                Output(string.Format(lang.GetMessage("DecayBlocked", this), block.ShortPrefabName, block.transform.position));
                
                //Prevent repair cooldown if no decay damage is done
                if (!info.hasDamage)
                {
                    return true;
                }

                return null;
            }

            string matchingType = null;
            if (_config.multipliers.ContainsKey(entity.ShortPrefabName) || IsOfType(entity, out matchingType))
            {
                if (_config.General.excludeOthers)
                {
                    return null;
                }
                
                info.damageTypes.ScaleAll(_config.multipliers[matchingType ?? entity.ShortPrefabName]);

                Output(string.Format(lang.GetMessage("DecayBlocked", this), entity.ShortPrefabName, entity.transform.position));
                    
                //Prevent repair cooldown if no decay damage is done
                if (!info.hasDamage)
                {
                    return true;
                }
            }

            if (_config.General.disableAll)
            {
                return true;
            }
            
            return null;
        }

        private object CanMoveItem(Item item, PlayerInventory playerLoot, ItemContainerId targetContainer, int targetSlot, int amount)
        {
            BaseEntity entity = playerLoot.FindContainer(targetContainer)?.entityOwner;

            if (!(entity is BuildingPrivlidge)) return null;

            bool flag = false;

            switch (item.info.shortname)
            {
                case "wood":
                {
                    if (_config.General.CupboardSettings.blockWood)
                    {
                        flag = true;
                    }

                    break;
                }

                case "stones":
                {
                    if (_config.General.CupboardSettings.blockStone)
                    {
                        flag = true;
                    }

                    break;
                }

                case "metal.fragments":
                {
                    if (_config.General.CupboardSettings.blockMetal)
                    {
                        flag = true;
                    }
                    
                    break;
                }

                case "metal.refined":
                {
                    if (_config.General.CupboardSettings.blockHighQ)
                    {
                        flag = true;
                    }

                    break;
                }
            }

            if (flag)
            {
                Output(lang.GetMessage("ItemMoveBlocked", this).Replace("{0}", item.info.shortname).Replace("{1}", $"{entity.ShortPrefabName}"));
                return true;
            }

            return null;
        }

        private bool IsOfType(BaseCombatEntity entity, out string matchingType)
        {
            matchingType = null;

            foreach (var pair in _config.multipliers)
            {
                if (pair.Key == entity.GetType()?.Name)
                {
                    matchingType = pair.Key;
                    return true;
                }
            }

            return false;
        }

        private bool AnyToolCupboards(BaseEntity entity)
        {
            if (entity is BuildingBlock)
            {
                BuildingBlock block = (BuildingBlock)entity;

                BuildingManager.Building building = block?.GetBuilding();
                if (building != null && building.GetDominatingBuildingPrivilege() != null)
                    return true;
            }

            Collider[] hits = Physics.OverlapSphere(entity.transform.position, _config.General.CupboardSettings.cupboardRange, Layers.Mask.Deployed);

            if (hits.Length > 0)
            {
                for (int i = 0; i < hits.Length; i++)
                {
                    if (hits[i]?.ToBaseEntity() is BuildingPrivlidge)
                        return true;
                }
            }

            return false;
        }

        private string GetOwnerPlayer(BuildingPrivlidge priv, ulong id = 0UL)
        {
            if (priv == null || !priv.AnyAuthed()) return string.Empty;

            if (_config.General.CupboardSettings.anyAuthed)
            {
                foreach (PlayerNameID player in priv.authorizedPlayers)
                {
                    if (player == null)
                    {
                        continue;
                    }
                    
                    string userId = player.userid.ToString();
                    
                    if (permission.UserHasPermission(userId, _config.General.permission))
                    {
                        return userId;
                    }
                }
            }
            else
            {
                PlayerNameID targetPlayer = null;

                foreach (PlayerNameID player in priv.authorizedPlayers)
                {
                    if (player.userid != id)
                    {
                        continue;
                    }

                    targetPlayer = player;
                    break;
                }
                
                if (targetPlayer != null)
                    return targetPlayer.userid.ToString();
            }

            return string.Empty;
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
                    public bool logToFile = true;

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
                ["NoPermission"] = "{0} does not have permission",
                ["ItemMoveBlocked"] = "{0} was blocked from being added to TC at {1}",
                ["DecayBlocked"] = "Decay was overriden on {0} at {1}",
                ["OutOfRange"] = "{0} was out of TC range, at {1}",
            }, this);
        }

        #endregion -Localisation-
    }
}