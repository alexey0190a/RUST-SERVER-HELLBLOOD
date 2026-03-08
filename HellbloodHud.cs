using System;
using System.Collections.Generic;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("HellbloodHud", "BLOODHELL", "1.0.1")]
    [Description("Simple HUD button with dropdown actions and active events line.")]
    public class HellbloodHud : RustPlugin
    {
        private const string UiMain = "HellbloodHud.UI.Main";
        private const string UiDropdown = "HellbloodHud.UI.Dropdown";

        private ConfigData _config;
        private readonly Dictionary<ulong, bool> _dropdownState = new Dictionary<ulong, bool>();
        private readonly Dictionary<string, bool> _customEvents = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private bool _patrolHelicopterActive;
        private bool _cargoShipActive;
        private bool _bradleyActive;
        private bool _chinookActive;

        private class ConfigData
        {
            public string MainButtonText = "МЕНЮ";
            public bool UseImageMainButton = false;
            public string NoActiveEventsText = "Активные ивенты: нет";
            public UiPosition UiPosition = new UiPosition();
            public UiGeometry UiGeometry = new UiGeometry();
            public UiColors UiColors = new UiColors();
            public List<DropdownButtonConfig> DropdownButtons = new List<DropdownButtonConfig>
            {
                new DropdownButtonConfig { Text = "Киты", Command = "kit", CommandType = "console" },
                new DropdownButtonConfig { Text = "Телепорт", Command = "tpr", CommandType = "console" },
                new DropdownButtonConfig { Text = "Магазин", Command = "shop", CommandType = "console" },
                new DropdownButtonConfig { Text = "Кланы", Command = "clan", CommandType = "console" },
                new DropdownButtonConfig { Text = "Инфо", Command = "info", CommandType = "console" }
            };
        }

        private class UiPosition
        {
            public string MainAnchorMin = "0.02 0.93";
            public string MainAnchorMax = "0.18 0.97";
            public string MainOffsetMin = "0 0";
            public string MainOffsetMax = "0 0";

            public string EventsAnchorMin = "0.02 0.97";
            public string EventsAnchorMax = "0.35 0.995";
            public string EventsOffsetMin = "0 0";
            public string EventsOffsetMax = "0 0";

            public string DropdownAnchorMin = "0.02 0.73";
            public string DropdownAnchorMax = "0.18 0.93";
            public string DropdownOffsetMin = "0 0";
            public string DropdownOffsetMax = "0 0";
        }

        private class UiGeometry
        {
            public int MainButtonFontSize = 14;
            public int EventsFontSize = 12;
            public int DropdownButtonFontSize = 12;
            public float DropdownButtonSpacing = 0.005f;
        }

        private class UiColors
        {
            public string MainButtonColor = "0.13 0.13 0.13 0.95";
            public string MainButtonTextColor = "1 1 1 1";

            public string DropdownButtonColor = "0.18 0.18 0.18 0.95";
            public string DropdownButtonTextColor = "1 1 1 1";

            public string EventsPanelColor = "0.08 0.08 0.08 0.9";
            public string EventsTextColor = "1 1 1 1";
        }

        private class DropdownButtonConfig
        {
            public string Text;
            public string Command;
            public string CommandType;
        }

        protected override void LoadDefaultConfig()
        {
            _config = new ConfigData();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<ConfigData>();
            }
            catch
            {
                _config = new ConfigData();
            }

            if (_config == null)
            {
                _config = new ConfigData();
            }

            if (_config.UiPosition == null)
            {
                _config.UiPosition = new UiPosition();
            }

            if (_config.UiGeometry == null)
            {
                _config.UiGeometry = new UiGeometry();
            }

            if (_config.UiColors == null)
            {
                _config.UiColors = new UiColors();
            }

            if (_config.DropdownButtons == null)
            {
                _config.DropdownButtons = new List<DropdownButtonConfig>();
            }

            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        private void Init()
        {
            cmd.AddConsoleCommand("hellbloodhud.toggle", this, nameof(CmdToggle));
            cmd.AddConsoleCommand("hellbloodhud.action", this, nameof(CmdAction));
        }

        private void OnServerInitialized()
        {
            RefreshVanillaEventsState();
            ShowHudForAllPlayers();
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyPlayerUi(player);
            }

            _dropdownState.Clear();
            _customEvents.Clear();
        }

        private void OnPlayerInit(BasePlayer player)
        {
            ShowPlayerUi(player);
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null)
            {
                return;
            }

            _dropdownState.Remove(player.userID);
            DestroyPlayerUi(player);
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            if (entity == null)
            {
                return;
            }

            if (entity is PatrolHelicopter)
            {
                _patrolHelicopterActive = true;
                RefreshHudForAllPlayers();
                return;
            }

            if (entity is CargoShip)
            {
                _cargoShipActive = true;
                RefreshHudForAllPlayers();
                return;
            }

            if (entity is BradleyAPC)
            {
                _bradleyActive = true;
                RefreshHudForAllPlayers();
                return;
            }

            if (entity is CH47HelicopterAIController)
            {
                _chinookActive = true;
                RefreshHudForAllPlayers();
            }
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            if (entity == null)
            {
                return;
            }

            if (entity is PatrolHelicopter || entity is CargoShip || entity is BradleyAPC || entity is CH47HelicopterAIController)
            {
                RefreshVanillaEventsState();
                RefreshHudForAllPlayers();
            }
        }

        private void CmdToggle(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (player == null)
            {
                return;
            }

            var current = false;
            _dropdownState.TryGetValue(player.userID, out current);
            _dropdownState[player.userID] = !current;
            ShowPlayerUi(player);
        }

        private void CmdAction(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (player == null)
            {
                return;
            }

            if (arg.Args == null || arg.Args.Length == 0)
            {
                return;
            }

            int index;
            if (!int.TryParse(arg.Args[0], out index))
            {
                return;
            }

            if (index < 0 || index >= _config.DropdownButtons.Count)
            {
                return;
            }

            var button = _config.DropdownButtons[index];
            if (button == null || string.IsNullOrWhiteSpace(button.Command))
            {
                return;
            }

            ExecutePlayerCommand(player, button.Command.Trim(), button.CommandType);
        }

        private void ExecutePlayerCommand(BasePlayer player, string command, string commandType)
        {
            if (player == null || string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            if (string.Equals(commandType, "chat", StringComparison.OrdinalIgnoreCase))
            {
                if (!command.StartsWith("/"))
                {
                    command = "/" + command;
                }

                player.SendConsoleCommand("chat.say", command);
                return;
            }

            player.SendConsoleCommand(command);
        }

        private void RefreshVanillaEventsState()
        {
            _patrolHelicopterActive = false;
            _cargoShipActive = false;
            _bradleyActive = false;
            _chinookActive = false;

            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity == null)
                {
                    continue;
                }

                if (!_patrolHelicopterActive && entity is PatrolHelicopter)
                {
                    _patrolHelicopterActive = true;
                }
                else if (!_cargoShipActive && entity is CargoShip)
                {
                    _cargoShipActive = true;
                }
                else if (!_bradleyActive && entity is BradleyAPC)
                {
                    _bradleyActive = true;
                }
                else if (!_chinookActive && entity is CH47HelicopterAIController)
                {
                    _chinookActive = true;
                }

                if (_patrolHelicopterActive && _cargoShipActive && _bradleyActive && _chinookActive)
                {
                    break;
                }
            }
        }

        private void ShowHudForAllPlayers()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                ShowPlayerUi(player);
            }
        }

        private void RefreshHudForAllPlayers()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                ShowPlayerUi(player);
            }
        }

        private void ShowPlayerUi(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
            {
                return;
            }

            DestroyPlayerUi(player);

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = _config.UiColors.EventsPanelColor },
                RectTransform =
                {
                    AnchorMin = _config.UiPosition.EventsAnchorMin,
                    AnchorMax = _config.UiPosition.EventsAnchorMax,
                    OffsetMin = _config.UiPosition.EventsOffsetMin,
                    OffsetMax = _config.UiPosition.EventsOffsetMax
                }
            }, "Hud", UiMain);

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = BuildEventsText(),
                    FontSize = _config.UiGeometry.EventsFontSize,
                    Align = TextAnchor.MiddleLeft,
                    Color = _config.UiColors.EventsTextColor
                },
                RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.98 1" }
            }, UiMain);

            container.Add(new CuiButton
            {
                Button =
                {
                    Color = _config.UiColors.MainButtonColor,
                    Command = "hellbloodhud.toggle"
                },
                Text =
                {
                    Text = _config.MainButtonText,
                    FontSize = _config.UiGeometry.MainButtonFontSize,
                    Align = TextAnchor.MiddleCenter,
                    Color = _config.UiColors.MainButtonTextColor
                },
                RectTransform =
                {
                    AnchorMin = _config.UiPosition.MainAnchorMin,
                    AnchorMax = _config.UiPosition.MainAnchorMax,
                    OffsetMin = _config.UiPosition.MainOffsetMin,
                    OffsetMax = _config.UiPosition.MainOffsetMax
                }
            }, "Hud", UiMain + ".MainButton");

            CuiHelper.AddUi(player, container);

            var opened = false;
            _dropdownState.TryGetValue(player.userID, out opened);
            if (opened)
            {
                ShowDropdown(player);
            }
        }

        private void ShowDropdown(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            CuiHelper.DestroyUi(player, UiDropdown);

            var buttons = _config.DropdownButtons;
            if (buttons == null || buttons.Count == 0)
            {
                return;
            }

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform =
                {
                    AnchorMin = _config.UiPosition.DropdownAnchorMin,
                    AnchorMax = _config.UiPosition.DropdownAnchorMax,
                    OffsetMin = _config.UiPosition.DropdownOffsetMin,
                    OffsetMax = _config.UiPosition.DropdownOffsetMax
                }
            }, "Hud", UiDropdown);

            var count = buttons.Count;
            var spacing = _config.UiGeometry.DropdownButtonSpacing;
            if (spacing < 0f)
            {
                spacing = 0f;
            }

            var totalSpacing = spacing * (count - 1);
            var buttonHeight = (1f - totalSpacing) / count;
            if (buttonHeight <= 0f)
            {
                buttonHeight = 1f / count;
                spacing = 0f;
            }

            for (var i = 0; i < count; i++)
            {
                var btn = buttons[i];
                if (btn == null)
                {
                    continue;
                }

                var maxY = 1f - (i * (buttonHeight + spacing));
                var minY = maxY - buttonHeight;
                if (minY < 0f)
                {
                    minY = 0f;
                }

                container.Add(new CuiButton
                {
                    Button =
                    {
                        Color = _config.UiColors.DropdownButtonColor,
                        Command = $"hellbloodhud.action {i}"
                    },
                    Text =
                    {
                        Text = btn.Text ?? string.Empty,
                        FontSize = _config.UiGeometry.DropdownButtonFontSize,
                        Align = TextAnchor.MiddleCenter,
                        Color = _config.UiColors.DropdownButtonTextColor
                    },
                    RectTransform =
                    {
                        AnchorMin = $"0 {minY}",
                        AnchorMax = $"1 {maxY}"
                    }
                }, UiDropdown);
            }

            CuiHelper.AddUi(player, container);
        }

        private void DestroyPlayerUi(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            CuiHelper.DestroyUi(player, UiMain);
            CuiHelper.DestroyUi(player, UiMain + ".MainButton");
            CuiHelper.DestroyUi(player, UiDropdown);
        }

        private string BuildEventsText()
        {
            var active = new List<string>();

            if (_patrolHelicopterActive)
            {
                active.Add("Patrol Helicopter");
            }

            if (_cargoShipActive)
            {
                active.Add("Cargo Ship");
            }

            if (_bradleyActive)
            {
                active.Add("Bradley APC");
            }

            if (_chinookActive)
            {
                active.Add("Chinook");
            }

            foreach (var pair in _customEvents)
            {
                if (pair.Value && !string.IsNullOrWhiteSpace(pair.Key))
                {
                    active.Add(pair.Key);
                }
            }

            if (active.Count == 0)
            {
                return _config.NoActiveEventsText;
            }

            return "Активные ивенты: " + string.Join(", ", active);
        }

        private void API_SetCustomEventState(string eventName, bool isActive)
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                return;
            }

            _customEvents[eventName.Trim()] = isActive;
            RefreshHudForAllPlayers();
        }

        private bool API_GetCustomEventState(string eventName)
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                return false;
            }

            bool isActive;
            return _customEvents.TryGetValue(eventName.Trim(), out isActive) && isActive;
        }
    }
}
