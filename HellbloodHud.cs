using System;
using System.Collections.Generic;
using System.Globalization;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("HellbloodHud", "BLOODHELL", "1.2.0")]
    [Description("HUD with info button, time panel and event indicators.")]
    public class HellbloodHud : RustPlugin
    {
        private const string UiMain = "HellbloodHud.UI.Main";
        private const string UiButton = "HellbloodHud.UI.Button";
        private const string UiEditor = "HellbloodHud.UI.Editor";

        private ConfigData _config;
        private readonly Dictionary<string, bool> _customEvents = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ulong, EditorState> _editorStateByPlayer = new Dictionary<ulong, EditorState>();

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
            public float EditorStep = 0.002f;
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

        private class EditorState
        {
            public int TargetIndex;
            public bool IsOpen;
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

            if (_config.UiGeometry.EditorStep <= 0f)
            {
                _config.UiGeometry.EditorStep = 0.002f;
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
            cmd.AddConsoleCommand("hellbloodhud.edit", this, nameof(CmdEdit));
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
                DestroyEditorUi(player);
            }

            _customEvents.Clear();
            _editorStateByPlayer.Clear();
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
            DestroyEditorUi(player);
            _editorStateByPlayer.Remove(player.userID);
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

        private void CmdEdit(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (player == null || !player.IsAdmin)
            {
                return;
            }

            if (arg.Args == null || arg.Args.Length == 0)
            {
                SendReply(player, "Команды: open|close|next|prev|step <value>|nudge <x> <y>|resize <x> <y>|save");
                return;
            }

            var action = arg.Args[0].ToLowerInvariant();
            EditorState state;
            if (!_editorStateByPlayer.TryGetValue(player.userID, out state))
            {
                state = new EditorState();
                _editorStateByPlayer[player.userID] = state;
            }

            if (action == "open")
            {
                state.IsOpen = true;
                RefreshEditorOverlay(player);
                return;
            }

            if (action == "close")
            {
                state.IsOpen = false;
                DestroyEditorUi(player);
                return;
            }

            if (!state.IsOpen)
            {
                return;
            }

            if (action == "next")
            {
                state.TargetIndex = (state.TargetIndex + 1) % 2;
                RefreshEditorOverlay(player);
                return;
            }

            if (action == "prev")
            {
                state.TargetIndex--;
                if (state.TargetIndex < 0)
                {
                    state.TargetIndex = 1;
                }
                RefreshEditorOverlay(player);
                return;
            }

            if (action == "step" && arg.Args.Length >= 2)
            {
                float step;
                if (!float.TryParse(arg.Args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out step) || step <= 0f)
                {
                    return;
                }

                _config.UiGeometry.EditorStep = step;
                RefreshEditorOverlay(player);
                return;
            }

            if (action == "nudge" && arg.Args.Length >= 3)
            {
                int dx;
                int dy;
                if (!int.TryParse(arg.Args[1], out dx) || !int.TryParse(arg.Args[2], out dy))
                {
                    return;
                }

                ApplyNudge(state.TargetIndex, dx * _config.UiGeometry.EditorStep, dy * _config.UiGeometry.EditorStep);
                RefreshHudPreview(player);
                RefreshEditorOverlay(player);
                return;
            }

            if (action == "resize" && arg.Args.Length >= 3)
            {
                int dx;
                int dy;
                if (!int.TryParse(arg.Args[1], out dx) || !int.TryParse(arg.Args[2], out dy))
                {
                    return;
                }

                ApplyResize(state.TargetIndex, dx * _config.UiGeometry.EditorStep, dy * _config.UiGeometry.EditorStep);
                RefreshHudPreview(player);
                RefreshEditorOverlay(player);
                return;
            }

            if (action == "save")
            {
                SaveConfig();
                return;
            }
        }

        private void ApplyNudge(int targetIndex, float x, float y)
        {
            if (targetIndex == 0)
            {
                ShiftAnchors(ref _config.UiPosition.ButtonAnchorMin, ref _config.UiPosition.ButtonAnchorMax, x, y);
                return;
            }

            ShiftAnchors(ref _config.UiPosition.InfoAnchorMin, ref _config.UiPosition.InfoAnchorMax, x, y);
        }

        private void ApplyResize(int targetIndex, float x, float y)
        {
            if (targetIndex == 0)
            {
                StretchAnchors(ref _config.UiPosition.ButtonAnchorMax, x, y);
                return;
            }

            StretchAnchors(ref _config.UiPosition.InfoAnchorMax, x, y);
        }

        private void ShiftAnchors(ref string min, ref string max, float x, float y)
        {
            Vector2 minV;
            Vector2 maxV;
            if (!TryParseAnchor(min, out minV) || !TryParseAnchor(max, out maxV))
            {
                return;
            }

            minV.x += x;
            minV.y += y;
            maxV.x += x;
            maxV.y += y;

            min = ToAnchor(minV);
            max = ToAnchor(maxV);
        }

        private void StretchAnchors(ref string max, float x, float y)
        {
            Vector2 maxV;
            if (!TryParseAnchor(max, out maxV))
            {
                return;
            }

            maxV.x += x;
            maxV.y += y;
            max = ToAnchor(maxV);
        }

        private bool TryParseAnchor(string value, out Vector2 result)
        {
            result = Vector2.zero;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var parts = value.Split(' ');
            if (parts.Length != 2)
            {
                return false;
            }

            float x;
            float y;
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
            {
                return false;
            }

            result = new Vector2(x, y);
            return true;
        }

        private string ToAnchor(Vector2 value)
        {
            return value.x.ToString("0.###", CultureInfo.InvariantCulture) + " " + value.y.ToString("0.###", CultureInfo.InvariantCulture);
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

        private void RefreshHudPreview(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
            {
                return;
            }

            DestroyPlayerUi(player);
            ShowPlayerUi(player);
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
                    Text = BuildMoscowTimeText(),
                    FontSize = _config.UiGeometry.TimeFontSize,
                    Align = TextAnchor.MiddleLeft,
                    Color = _config.UiColors.InfoTextColor
                },
                RectTransform = { AnchorMin = "0.015 0.5", AnchorMax = "0.22 1" }
            }, UiMain);

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = BuildOnlineText(),
                    FontSize = _config.UiGeometry.OnlineFontSize,
                    Align = TextAnchor.MiddleLeft,
                    Color = _config.UiColors.InfoTextColor
                },
                RectTransform = { AnchorMin = "0.225 0.5", AnchorMax = "0.40 1" }
            }, UiMain);

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = BuildServerTimeText(),
                    FontSize = _config.UiGeometry.TimeFontSize,
                    Align = TextAnchor.MiddleLeft,
                    Color = _config.UiColors.InfoTextColor
                },
                RectTransform = { AnchorMin = "0.015 0", AnchorMax = "0.40 0.5" }
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
                RectTransform = { AnchorMin = "0.42 0.08", AnchorMax = "0.985 0.46" }
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
            return "Онлайн: " + BasePlayer.activePlayerList.Count;
        }

        private string BuildMoscowTimeText()
        {
            return "Москва: " + DateTime.UtcNow.AddHours(3).ToString("HH:mm");
        }

        private string BuildServerTimeText()
        {
            var cycle = TOD_Sky.Instance?.Cycle;
            if (cycle == null)
            {
                return "Сервер: --:--";
            }

            return "Сервер: " + cycle.DateTime.ToString("HH:mm");
        }

        private void RefreshEditorOverlay(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
            {
                return;
            }

            EditorState state;
            if (!_editorStateByPlayer.TryGetValue(player.userID, out state) || !state.IsOpen)
            {
                DestroyEditorUi(player);
                return;
            }

            DestroyEditorUi(player);

            var targetName = state.TargetIndex == 0 ? "Кнопка" : "Панель";
            var targetMin = state.TargetIndex == 0 ? _config.UiPosition.ButtonAnchorMin : _config.UiPosition.InfoAnchorMin;
            var targetMax = state.TargetIndex == 0 ? _config.UiPosition.ButtonAnchorMax : _config.UiPosition.InfoAnchorMax;

            var container = new CuiElementContainer();
            var panel = container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.8" },
                RectTransform = { AnchorMin = "0.35 0.02", AnchorMax = "0.65 0.16" },
                CursorEnabled = true
            }, "Overlay", UiEditor);

            container.Add(new CuiElement
            {
                Parent = panel,
                Components =
                {
                    new CuiNeedsCursorComponent()
                }
            });

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = $"HUD EDIT | Цель: {targetName} | Шаг: {_config.UiGeometry.EditorStep.ToString("0.###", CultureInfo.InvariantCulture)}\nMin: {targetMin} Max: {targetMax}",
                    FontSize = 11,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 1 1 1"
                },
                RectTransform = { AnchorMin = "0.02 0.52", AnchorMax = "0.98 0.98" }
            }, panel);

            AddEditorButton(container, panel, "<", "0.02 0.10", "0.10 0.48", "hellbloodhud.edit prev");
            AddEditorButton(container, panel, ">", "0.11 0.10", "0.19 0.48", "hellbloodhud.edit next");
            AddEditorButton(container, panel, "L", "0.22 0.10", "0.28 0.48", "hellbloodhud.edit nudge -1 0");
            AddEditorButton(container, panel, "R", "0.29 0.10", "0.35 0.48", "hellbloodhud.edit nudge 1 0");
            AddEditorButton(container, panel, "U", "0.36 0.10", "0.42 0.48", "hellbloodhud.edit nudge 0 1");
            AddEditorButton(container, panel, "D", "0.43 0.10", "0.49 0.48", "hellbloodhud.edit nudge 0 -1");
            AddEditorButton(container, panel, "W+", "0.52 0.10", "0.59 0.48", "hellbloodhud.edit resize 1 0");
            AddEditorButton(container, panel, "W-", "0.60 0.10", "0.67 0.48", "hellbloodhud.edit resize -1 0");
            AddEditorButton(container, panel, "H+", "0.68 0.10", "0.75 0.48", "hellbloodhud.edit resize 0 1");
            AddEditorButton(container, panel, "H-", "0.76 0.10", "0.83 0.48", "hellbloodhud.edit resize 0 -1");
            AddEditorButton(container, panel, "SAVE", "0.84 0.10", "0.92 0.48", "hellbloodhud.edit save");
            AddEditorButton(container, panel, "X", "0.93 0.10", "0.98 0.48", "hellbloodhud.edit close");

            CuiHelper.AddUi(player, container);
        }

        private void AddEditorButton(CuiElementContainer container, string parent, string text, string min, string max, string command)
        {
            container.Add(new CuiButton
            {
                Button = { Color = "0.2 0.2 0.2 0.95", Command = command },
                Text = { Text = text, FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = min, AnchorMax = max }
            }, parent);
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

        private void DestroyEditorUi(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            CuiHelper.DestroyUi(player, UiEditor);
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
