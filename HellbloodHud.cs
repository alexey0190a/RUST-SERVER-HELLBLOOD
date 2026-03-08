using System;
using System.Collections.Generic;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("HellbloodHud", "BLOODHELL", "1.1.0")]
    [Description("HUD with one info button, online/time line and event indicators.")]
    public class HellbloodHud : RustPlugin
    {
        private const string UiMain = "HellbloodHud.UI.Main";
        private const string UiButton = "HellbloodHud.UI.Button";

        private ConfigData _config;
        private readonly Dictionary<string, bool> _customEvents = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private bool _patrolHelicopterActive;
        private bool _cargoShipActive;
        private bool _bradleyActive;
        private bool _chinookActive;

        private Timer _refreshTimer;

        private class ConfigData
        {
            public UiPosition UiPosition = new UiPosition();
            public UiGeometry UiGeometry = new UiGeometry();
            public UiColors UiColors = new UiColors();
        }

        private class UiPosition
        {
            public string ButtonAnchorMin = "0.02 0.93";
            public string ButtonAnchorMax = "0.02 0.93";
            public string ButtonOffsetMin = "0 0";
            public string ButtonOffsetMax = "44 44";

            public string InfoAnchorMin = "0.065 0.93";
            public string InfoAnchorMax = "0.45 0.97";
            public string InfoOffsetMin = "0 0";
            public string InfoOffsetMax = "0 0";
        }

        private class UiGeometry
        {
            public int ButtonFontSize = 14;
            public int OnlineFontSize = 12;
            public int TimeFontSize = 12;
            public float EventIndicatorSpacing = 0.01f;
        }

        private class UiColors
        {
            public string ButtonColor = "0.14 0.14 0.14 0.95";
            public string ButtonTextColor = "1 1 1 1";

            public string InfoPanelColor = "0.08 0.08 0.08 0.9";
            public string InfoTextColor = "1 1 1 1";

            public string EventActiveColor = "0.2 0.7 0.2 1";
            public string EventInactiveColor = "0.35 0.35 0.35 1";
            public string EventTextColor = "1 1 1 1";
        }

        private class EventIndicator
        {
            public string Label;
            public bool IsActive;
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

            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        private void Init()
        {
            cmd.AddConsoleCommand("hellbloodhud.info", this, nameof(CmdInfo));
        }

        private void OnServerInitialized()
        {
            RefreshVanillaEventsState();
            ShowHudForAllPlayers();

            _refreshTimer?.Destroy();
            _refreshTimer = timer.Every(5f, RefreshHudForAllPlayers);
        }

        private void Unload()
        {
            _refreshTimer?.Destroy();
            _refreshTimer = null;

            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyPlayerUi(player);
            }

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

        private void CmdInfo(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (player == null)
            {
                return;
            }

            player.SendConsoleCommand("chat.say", "/info");
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

            container.Add(new CuiButton
            {
                Button =
                {
                    Color = _config.UiColors.ButtonColor,
                    Command = "hellbloodhud.info"
                },
                Text =
                {
                    Text = "●",
                    FontSize = _config.UiGeometry.ButtonFontSize,
                    Align = TextAnchor.MiddleCenter,
                    Color = _config.UiColors.ButtonTextColor
                },
                RectTransform =
                {
                    AnchorMin = _config.UiPosition.ButtonAnchorMin,
                    AnchorMax = _config.UiPosition.ButtonAnchorMax,
                    OffsetMin = _config.UiPosition.ButtonOffsetMin,
                    OffsetMax = _config.UiPosition.ButtonOffsetMax
                }
            }, "Hud", UiButton);

            container.Add(new CuiPanel
            {
                Image = { Color = _config.UiColors.InfoPanelColor },
                RectTransform =
                {
                    AnchorMin = _config.UiPosition.InfoAnchorMin,
                    AnchorMax = _config.UiPosition.InfoAnchorMax,
                    OffsetMin = _config.UiPosition.InfoOffsetMin,
                    OffsetMax = _config.UiPosition.InfoOffsetMax
                }
            }, "Hud", UiMain);

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = BuildOnlineText(),
                    FontSize = _config.UiGeometry.OnlineFontSize,
                    Align = TextAnchor.MiddleLeft,
                    Color = _config.UiColors.InfoTextColor
                },
                RectTransform = { AnchorMin = "0.015 0", AnchorMax = "0.22 1" }
            }, UiMain);

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = BuildTimeText(),
                    FontSize = _config.UiGeometry.TimeFontSize,
                    Align = TextAnchor.MiddleLeft,
                    Color = _config.UiColors.InfoTextColor
                },
                RectTransform = { AnchorMin = "0.225 0", AnchorMax = "0.40 1" }
            }, UiMain);

            AddEventIndicators(container);

            CuiHelper.AddUi(player, container);
        }

        private void AddEventIndicators(CuiElementContainer container)
        {
            if (container == null)
            {
                return;
            }

            var indicators = BuildEventIndicators();
            if (indicators.Count == 0)
            {
                return;
            }

            var spacing = _config.UiGeometry.EventIndicatorSpacing;
            if (spacing < 0f)
            {
                spacing = 0f;
            }

            var count = indicators.Count;
            var totalSpacing = spacing * (count - 1);
            var width = (1f - totalSpacing) / count;
            if (width <= 0f)
            {
                width = 1f / count;
                spacing = 0f;
            }

            var parent = container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.42 0.12", AnchorMax = "0.985 0.88" }
            }, UiMain, UiMain + ".Events");

            for (var i = 0; i < count; i++)
            {
                var minX = i * (width + spacing);
                var maxX = minX + width;

                var indicator = indicators[i];
                var indicatorPanel = container.Add(new CuiPanel
                {
                    Image = { Color = indicator.IsActive ? _config.UiColors.EventActiveColor : _config.UiColors.EventInactiveColor },
                    RectTransform =
                    {
                        AnchorMin = $"{minX} 0",
                        AnchorMax = $"{maxX} 1"
                    }
                }, parent, UiMain + ".Events.State." + i);

                container.Add(new CuiLabel
                {
                    Text =
                    {
                        Text = indicator.Label,
                        FontSize = 10,
                        Align = TextAnchor.MiddleCenter,
                        Color = _config.UiColors.EventTextColor
                    },
                    RectTransform =
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1"
                    }
                }, indicatorPanel);
            }
        }

        private List<EventIndicator> BuildEventIndicators()
        {
            var indicators = new List<EventIndicator>
            {
                new EventIndicator { Label = "HELI", IsActive = _patrolHelicopterActive },
                new EventIndicator { Label = "CARGO", IsActive = _cargoShipActive },
                new EventIndicator { Label = "BRAD", IsActive = _bradleyActive },
                new EventIndicator { Label = "CH47", IsActive = _chinookActive }
            };

            foreach (var pair in _customEvents)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                indicators.Add(new EventIndicator
                {
                    Label = pair.Key,
                    IsActive = pair.Value
                });
            }

            return indicators;
        }

        private string BuildOnlineText()
        {
            return "Online: " + BasePlayer.activePlayerList.Count;
        }

        private string BuildTimeText()
        {
            var cycle = TOD_Sky.Instance?.Cycle;
            if (cycle == null)
            {
                return "Time: --:--";
            }

            return "Time: " + cycle.DateTime.ToString("HH:mm");
        }

        private void DestroyPlayerUi(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            CuiHelper.DestroyUi(player, UiMain);
            CuiHelper.DestroyUi(player, UiButton);
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
