using System;
using System.Collections.Generic;

using UnityEngine;

using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("No Decay", "0x89A", "1.2.2")]
    [Description("Scales or disables decay of items and deployables")]
    class NoDecay : CovalencePlugin
    {
        private Configuration config;

        void Init() => permission.RegisterPermission(config.usePermission, this);

        void Output(string text)
        {
            if (config.General.rconOutput) Puts(text);
            if (config.General.logToFile) LogToFile(config.General.logFileName, text, this);
        }

        #region -Oxide Hooks-

        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info == null || info.damageTypes == null || entity == null || !info.damageTypes.Has(Rust.DamageType.Decay)) return null;

            BasePlayer player = BasePlayer.FindAwakeOrSleeping(entity.OwnerID.ToString());
            if (config.General.usePermission && entity.OwnerID != 0UL && (player != null && !permission.UserHasPermission(player.UserIDString, config.usePermission)))
            {
                Output(lang.GetMessage("NoPermission", this).Replace("{0}", $"{player.displayName} ({player.userID})"));
                return null;
            }
            
            if (config.General.requireTC && (entity.GetBuildingPrivilege() == null || Vector3.Distance(entity.CenterPoint(), entity.GetBuildingPrivilege().CenterPoint()) > config.General.cupboardRange))
            {
                Output(lang.GetMessage("OutOfRange", this).Replace("{0}", entity.ShortPrefabName).Replace("{1}", entity.ShortPrefabName));
                return null;
            }

            if (config.General.disableAll) return true;

            if (entity is BuildingBlock)
            {
                BuildingBlock block = entity as BuildingBlock;
                info.damageTypes.ScaleAll(config.buildingMultipliers[(int)block.grade]);

                if (!info.hasDamage) return true;

                Output(lang.GetMessage("DecayBlocked", this).Replace("{0}", entity.ShortPrefabName).Replace("{1}", $"{entity.transform.position}"));
                return null;
            }

            string matchingType = null;

            if (config.multipliers.ContainsKey(entity.ShortPrefabName) || IsOfType(entity, out matchingType))
            {
                info.damageTypes.ScaleAll(config.multipliers[matchingType != null ? matchingType : entity.ShortPrefabName]);

                if (!info.hasDamage) return true;

                Output(lang.GetMessage("DecayBlocked", this).Replace("{0}", entity.ShortPrefabName).Replace("{1}", $"{entity.transform.position}"));
            }

            return null;
        }

        object CanMoveItem(Item item, PlayerInventory playerLoot, uint targetContainer, int targetSlot, int amount)
        {
            BaseEntity entity = playerLoot.FindContainer(targetContainer)?.entityOwner;

            if (!(entity is BuildingPrivlidge)) return null;

            bool flag = false;

            //rip target-type conditional expression, fuck C# 7.3, 9.0 the move
            switch (item.info.shortname)
            {
                case "wood":
                    if (config.General.blockWood) flag = true;
                    break;

                case "stones":
                    if (config.General.blockStone) flag = true;
                    break;

                case "metal.fragments":
                    if (config.General.blockMetal) flag = true;
                    break;

                case "metal.refined":
                    if (config.General.blockHighQ) flag = true;
                    break;
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

            foreach (var pair in config.multipliers)
            {
                if (pair.Key == entity.GetType().FullName)
                {
                    matchingType = pair.Key;
                    return true;
                }
            }

            return false;
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

            [JsonProperty(PropertyName = "Use permission")]
            public string usePermission = "nodecay.use";

            #region -Classes-

            public class GeneralSettings
            {
                [JsonProperty(PropertyName = "Disable decay for all entities")]
                public bool disableAll = false;

                [JsonProperty(PropertyName = "Use permission")]
                public bool usePermission = true;

                [JsonProperty(PropertyName = "Output to server console")]
                public bool rconOutput = false;

                [JsonProperty(PropertyName = "Log to file")]
                public bool logToFile = true;

                [JsonProperty(PropertyName = "Log file name")]
                public string logFileName = "NoDecay-Log";

                [JsonProperty(PropertyName = "Require Tool Cupboard")]
                public bool requireTC = false;

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
                config = Config.ReadObject<Configuration>();
                if (config == null) throw new Exception();
                config.buildingMultipliers = new float[5] { config.BuildingTiers.twig, config.BuildingTiers.wood, config.BuildingTiers.stone, config.BuildingTiers.metal, config.BuildingTiers.armoured };
                SaveConfig();
            }
            catch
            {
                PrintError("Error loading config, using default values");
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig() => config = new Configuration();

        protected override void SaveConfig() => Config.WriteObject(config);

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
