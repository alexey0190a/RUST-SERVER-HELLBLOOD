using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
    [Info("Quarry Locks", "Orange", "1.3.4")]
    [Description("Add code-lock on Mining Quarry/Pump Jack")]
    public class QuarryLocks : CovalencePlugin
    {
        #region Class Fields

        [PluginReference] private Plugin Clans, Friends;
        private const string PermissionUse = "quarrylocks.use";

        #endregion Class Fields

        #region Initialization

        private void Init()
        {
            permission.RegisterPermission(PermissionUse, this);
            foreach (var command in _configData.Commands)
                AddCovalenceCommand(command, nameof(CmdLockQuarry));

            if (!_configData.AutoCodeLock)
                Unsubscribe(nameof(OnEntityBuilt));
        }

        private void OnServerInitialized()
        {
            UpdateConfig();
        }

        private void UpdateConfig()
        {
            if (_configData.Commands.Length == 0)
            {
                _configData.Commands = new[] { "ql" };
                SaveConfig();
            }
        }

        #endregion Initialization

        #region Configuration

        private ConfigData _configData;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Enable Permission")]
            public bool UsePermission = true;

            [JsonProperty(PropertyName = "Can Admins Ignore Permission Restriction")]
            public bool AdminsAllowed = true;

            [JsonProperty(PropertyName = "Automatic Code Setup")]
            public bool AutoCode = true;

            [JsonProperty(PropertyName = "Automatic Code Lock Deploy")]
            public bool AutoCodeLock = false;

            [JsonProperty(PropertyName = "Share With Team")]
            public bool UseTeams = false;

            [JsonProperty(PropertyName = "Share With Clan")]
            public bool UseClans = false;

            [JsonProperty(PropertyName = "Share With Friends")]
            public bool UseFriends = false;

            [JsonProperty(PropertyName = "Command")]
            public string[] Commands = new[] { "ql", "quarrylock" };

            [JsonProperty(PropertyName = "Chat Avatar")]
            public ulong SteamIDIcon = 0;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _configData = Config.ReadObject<ConfigData>();
                if (_configData == null)
                {
                    LoadDefaultConfig();
                }
            }
            catch
            {
                PrintError("The configuration file is corrupted");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            _configData = new ConfigData();
        }

        protected override void SaveConfig() => Config.WriteObject(_configData);

        #endregion Configuration

        #region Localization

        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Added"] = "Codelock was added!",
                ["Already"] = "That Mining Quarry/Pump Jack already has codelock",
                ["NoLock"] = "You need a codelock to use this command, get one and try again!",
                ["NotAllowed"] = "You do not have permission to use this command",
                ["NotAQuarry"] = "This is not Mining Quarry/Pump Jack",
                ["NotLookingAtQuarry"] = "You are not looking at Mining Quarry/Pump Jack",
                ["Ownership"] = "Only owner of Mining Quarry/Pump Jack can add locks to it!",
                ["Prefix"] = "<color=#00FFFF>[Quarry Locks]</color>: ",
            }, this);
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Added"] = "Кодовый замок добавлен!",
                ["Already"] = "На этот Горнорудный карьер/Нефтяную вышку уже установлен кодовый замок",
                ["NoLock"] = "Для использования этой команды вам понадобится кодовый замок, возьмите его и попробуйте еще раз!",
                ["NotAllowed"] = "У вас нет разрешения на использование этой команды",
                ["NotAQuarry"] = "Это не Горнорудный карьер/Нефтяная вышка",
                ["NotLookingAtQuarry"] = "Вы не смотрите на Горнорудный карьер/Нефтяную вышку",
                ["Ownership"] = "Только владелец Горнорудного карьера/Нефтяной вышки может поставить на него замок!",
                ["Prefix"] = "<color=#00FFFF>[Quarry Locks]</color>: ",
            }, this, "ru");
        }

        #endregion Localization

        #region Oxide Hooks

        private object CanLootEntity(BasePlayer player, ResourceExtractorFuelStorage container)
        {
            if (container == null || player == null || container.IsDestroyed)
                return null;

            if (_configData.AdminsAllowed && player.IsAdmin)
                return null;

            BaseResourceExtractor entity = container.GetComponentInParent<BaseResourceExtractor>();
            if (entity == null) return null;
            CodeLock codelock = entity.GetComponentInChildren<CodeLock>();
            if (codelock == null)
                return null;

            return codelock.whitelistPlayers.Contains(player.userID) ? (object)null : true;
        }

        private void OnEntityBuilt(Planner planner, GameObject gameObject)
        {
            if (!_configData.AutoCodeLock)
                return;

            BaseEntity entity = gameObject?.ToBaseEntity();
            if (!entity)
                return;

            BasePlayer deployingPlayer = planner?.GetOwnerPlayer();
            if (!deployingPlayer)
                return;

            if (entity.ShortPrefabName != "mining_quarry")
                return;

            Vector3 cordinates;
            if (entity.ShortPrefabName == "mining_quarry")
                cordinates = new Vector3(1.35f, 10.15f, 0.00f);
            else
                cordinates = new Vector3(-3.38f, 6.30f, -0.30f);

            if (deployingPlayer.inventory.FindItemByItemID(1159991980) == null)
            {
                Print(deployingPlayer.IPlayer, Lang("NoLock", deployingPlayer.IPlayer.Id));
                return;
            }

            deployingPlayer.inventory.Take(null, 1159991980, 1);

            var codelock = GameManager.server.CreateEntity(StringPool.Get(3518824735)) as CodeLock;
            var bre = entity.GetComponent<BaseResourceExtractor>();

            codelock.gameObject.Identity();
            codelock.SetParent(bre, BaseEntity.Slot.Lock.ToString().ToLower());
            codelock.Spawn();
            bre.SetSlot(BaseEntity.Slot.Lock, codelock);
            codelock.transform.localPosition += cordinates;
            codelock.TransformChanged();

            if (_configData.AutoCode)
            {
                codelock.code = Random.Range(1000, 9999).ToString();
                codelock.hasCode = true;
                codelock.SetFlag(BaseEntity.Flags.Locked, true);
                codelock.whitelistPlayers.Add(deployingPlayer.userID);

                if (_configData.UseTeams && RelationshipManager.TeamsEnabled())
                {
                    RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindPlayersTeam(deployingPlayer.userID);
                    if (playerTeam != null)
                    {
                        foreach (var member in playerTeam.members)
                        {
                            codelock.whitelistPlayers.Add(member);
                        }
                    }
                }

                if (_configData.UseClans && Clans != null && Clans.IsLoaded)
                {
                    string playerClan = Clans.Call<string>("GetClanOf", deployingPlayer);
                    if (!string.IsNullOrWhiteSpace(playerClan))
                    {
                        JObject clan = Clans.Call("GetClan", playerClan) as JObject;
                        JArray members = clan?.GetValue("members") as JArray;
                        if (clan != null && members != null)
                        {
                            foreach (var clanMember in members)
                            {
                                codelock.whitelistPlayers.Add(ulong.Parse(clanMember.ToString()));
                            }
                        }
                    }
                }

                if (_configData.UseFriends && Friends != null && Friends.IsLoaded)
                {
                    var friends = Friends.Call("GetFriends", deployingPlayer.userID) as ulong[];
                    if (friends != null)
                    {
                        foreach (var friend in friends)
                        {
                            codelock.whitelistPlayers.Add(friend);
                        }
                    }
                }
            }

            Print(deployingPlayer.IPlayer, Lang("Added", deployingPlayer.IPlayer.Id));
        }

        #endregion Oxide Hooks

        #region Commands

        private void CmdLockQuarry(IPlayer iplayer)
        {
            BasePlayer player = iplayer?.Object as BasePlayer;

            if (iplayer == null || player == null)
            {
                return;
            }

            if (_configData.UsePermission && !permission.UserHasPermission(iplayer.Id, PermissionUse))
            {
                if (!_configData.AdminsAllowed || !iplayer.IsAdmin)
                {
                    Print(iplayer, Lang("NotAllowed", iplayer.Id));
                    return;
                }
            }

            RaycastHit rhit;
            BaseEntity entity = null;
            if (Physics.Raycast(player.eyes.HeadRay(), out rhit))
            {
                entity = rhit.GetEntity();
            }

            if (entity == null)
            {
                Print(iplayer, Lang("NotLookingAtQuarry", iplayer.Id));
                return;
            }

            Vector3 cordinates;
            switch (entity.ShortPrefabName)
            {
                case "mining.pumpjack":
                    cordinates = new Vector3(-3.38f, 6.30f, -0.30f);
                    break;
                case "mining_quarry":
                    cordinates = new Vector3(1.35f, 10.15f, 0.00f);
                    break;
                default:
                    Print(iplayer, Lang("NotAQuarry", iplayer.Id));
                    return;
            }

            if (entity.OwnerID != player.userID)
            {
                Print(iplayer, Lang("Ownership", iplayer.Id));
                return;
            }

            if (entity.GetComponentInChildren<CodeLock>() != null)
            {
                Print(iplayer, Lang("Already", iplayer.Id));
                return;
            }

            if (player.inventory.FindItemByItemID(1159991980) == null)
            {
                Print(iplayer, Lang("NoLock", iplayer.Id));
                return;
            }

            player.inventory.Take(null, 1159991980, 1);

            var codelock = GameManager.server.CreateEntity(StringPool.Get(3518824735)) as CodeLock;
            var bre = entity.GetComponent<BaseResourceExtractor>();

            codelock.gameObject.Identity();
            codelock.SetParent(bre, BaseEntity.Slot.Lock.ToString().ToLower());
            codelock.Spawn();
            bre.SetSlot(BaseEntity.Slot.Lock, codelock);
            codelock.transform.localPosition += cordinates;
            codelock.TransformChanged();

            if (_configData.AutoCode)
            {
                codelock.code = Random.Range(1000, 9999).ToString();
                codelock.hasCode = true;
                codelock.SetFlag(BaseEntity.Flags.Locked, true);
                codelock.whitelistPlayers.Add(player.userID);

                if (_configData.UseTeams && RelationshipManager.TeamsEnabled())
                {
                    RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindPlayersTeam(player.userID);
                    if (playerTeam != null)
                    {
                        foreach (var member in playerTeam.members)
                        {
                            codelock.whitelistPlayers.Add(member);
                        }
                    }
                }

                if (_configData.UseClans && Clans != null && Clans.IsLoaded)
                {
                    string playerClan = Clans.Call<string>("GetClanOf", player);
                    if (!string.IsNullOrWhiteSpace(playerClan))
                    {
                        JObject clan = Clans.Call("GetClan", playerClan) as JObject;
                        JArray members = clan?.GetValue("members") as JArray;
                        if (clan != null && members != null)
                        {
                            foreach (var clanMember in members)
                            {
                                codelock.whitelistPlayers.Add(ulong.Parse(clanMember.ToString()));
                            }
                        }
                    }
                }

                if (_configData.UseFriends && Friends != null && Friends.IsLoaded)
                {
                    var friends = Friends.Call("GetFriends", player.userID) as ulong[];
                    if (friends != null)
                    {
                        foreach (var friend in friends)
                        {
                            codelock.whitelistPlayers.Add(friend);
                        }
                    }
                }
            }

            Print(iplayer, Lang("Added", iplayer.Id));
        }

        #endregion Commands

        #region Helpers

        private void Print(IPlayer player, string message)
        {
            string text;
            if (string.IsNullOrEmpty(Lang("Prefix", player.Id)))
            {
                text = message;
            }
            else
            {
                text = Lang("Prefix", player.Id) + message;
            }
#if RUST
            (player.Object as BasePlayer).SendConsoleCommand ("chat.add", 2, _configData.SteamIDIcon, text);
            return;
#endif
            player.Message(text);
        }

        #endregion Helpers
    }
}