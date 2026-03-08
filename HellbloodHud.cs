using System;
using System.Collections.Generic;
using System.Globalization;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Oxide.Core.Plugins;


namespace Oxide.Plugins
{
    [Info("HellbloodHud", "BLOODHELL", "1.3.0")]
    [Description("HUD with info button, time panel and fixed event tiles.")]
    public class HellbloodHud : RustPlugin
    {
        private const string UiMain = "HellbloodHud.UI.Main";
        private const string UiButton = "HellbloodHud.UI.Button";
        private const string UiEditor = "HellbloodHud.UI.Editor";
        private const string UiEditorStatus = "HellbloodHud.UI.Editor.Status";
		private const string UiMainBackground = "HellbloodHud.UI.Main.Background";

		[PluginReference] private Plugin ImageLibrary;

        private ConfigData _config;
        private readonly Dictionary<string, bool> _customEvents = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ulong, EditorState> _editorStateByPlayer = new Dictionary<ulong, EditorState>();

        private bool _patrolHelicopterActive;
        private bool _cargoShipActive;
        private bool _chinookActive;
        private bool _airdropActive;

        private Timer _refreshTimer;

        private class ConfigData
{
    public UiPosition UiPosition = new UiPosition();
    public UiGeometry UiGeometry = new UiGeometry();
    public UiColors UiColors = new UiColors();
    public UiImages UiImages = new UiImages();
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
			public string BackgroundAnchorMin = "0.065 0.93";
			public string BackgroundAnchorMax = "0.45 0.97";
			public string BackgroundOffsetMin = "0 0";
			public string BackgroundOffsetMax = "0 0";

            public string EventTileHelltrainMin = "0.42 0.28";
            public string EventTileHelltrainMax = "0.57 0.46";

            public string EventTileConvoyMin = "0.59 0.28";
            public string EventTileConvoyMax = "0.74 0.46";

            public string EventTileCargoMin = "0.42 0.08";
            public string EventTileCargoMax = "0.52 0.24";

            public string EventTileAirdropMin = "0.54 0.08";
            public string EventTileAirdropMax = "0.64 0.24";

            public string EventTileChinookMin = "0.66 0.08";
            public string EventTileChinookMax = "0.76 0.24";

            public string EventTileHelitiersMin = "0.78 0.08";
            public string EventTileHelitiersMax = "0.92 0.24";
        }

        private class UiGeometry
        {
            public int ButtonFontSize = 14;
            public int OnlineFontSize = 12;
            public int TimeFontSize = 12;
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
		
		private class UiImages
{
    public string MainBackgroundPng = "";
    public string MainBackgroundUrl = "";
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
			
			if (_config.UiImages == null)
{
    _config.UiImages = new UiImages();
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

            if (entity is CargoPlane)
{
    _airdropActive = true;
    RefreshHudForAllPlayers();
    return;
}

            if (entity is CH47HelicopterAIController)
            {
                _chinookActive = true;
                RefreshHudForAllPlayers();
                return;
            }
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            if (entity == null)
            {
                return;
            }

            if (entity is PatrolHelicopter || entity is CargoShip || entity is CH47HelicopterAIController || entity is CargoPlane)
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
                CreateEditorOverlay(player);
                UpdateEditorOverlayStatus(player, state);
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
                state.TargetIndex = (state.TargetIndex + 1) % 8;
                UpdateEditorOverlayStatus(player, state);
                return;
            }

            if (action == "prev")
            {
                state.TargetIndex--;
                if (state.TargetIndex < 0)
                {
                    state.TargetIndex = 7;
                }
                UpdateEditorOverlayStatus(player, state);
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
                UpdateEditorOverlayStatus(player, state);
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
                UpdateEditorOverlayStatus(player, state);
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
                UpdateEditorOverlayStatus(player, state);
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
            switch (targetIndex)
            {
                case 0:
                    ShiftAnchors(ref _config.UiPosition.ButtonAnchorMin, ref _config.UiPosition.ButtonAnchorMax, x, y);
                    return;
                case 1:
                    ShiftAnchors(ref _config.UiPosition.InfoAnchorMin, ref _config.UiPosition.InfoAnchorMax, x, y);
                    return;
                case 2:
                    ShiftAnchors(ref _config.UiPosition.EventTileHelltrainMin, ref _config.UiPosition.EventTileHelltrainMax, x, y);
                    return;
                case 3:
                    ShiftAnchors(ref _config.UiPosition.EventTileConvoyMin, ref _config.UiPosition.EventTileConvoyMax, x, y);
                    return;
                case 4:
                    ShiftAnchors(ref _config.UiPosition.EventTileCargoMin, ref _config.UiPosition.EventTileCargoMax, x, y);
                    return;
                case 5:
                    ShiftAnchors(ref _config.UiPosition.EventTileAirdropMin, ref _config.UiPosition.EventTileAirdropMax, x, y);
                    return;
                case 6:
                    ShiftAnchors(ref _config.UiPosition.EventTileChinookMin, ref _config.UiPosition.EventTileChinookMax, x, y);
                    return;
                case 7:
                    ShiftAnchors(ref _config.UiPosition.EventTileHelitiersMin, ref _config.UiPosition.EventTileHelitiersMax, x, y);
                    return;
            }
        }

        private void ApplyResize(int targetIndex, float x, float y)
        {
            switch (targetIndex)
            {
                case 0:
                    StretchAnchors(ref _config.UiPosition.ButtonAnchorMax, x, y);
                    return;
                case 1:
                    StretchAnchors(ref _config.UiPosition.InfoAnchorMax, x, y);
                    return;
                case 2:
                    StretchAnchors(ref _config.UiPosition.EventTileHelltrainMax, x, y);
                    return;
                case 3:
                    StretchAnchors(ref _config.UiPosition.EventTileConvoyMax, x, y);
                    return;
                case 4:
                    StretchAnchors(ref _config.UiPosition.EventTileCargoMax, x, y);
                    return;
                case 5:
                    StretchAnchors(ref _config.UiPosition.EventTileAirdropMax, x, y);
                    return;
                case 6:
                    StretchAnchors(ref _config.UiPosition.EventTileChinookMax, x, y);
                    return;
                case 7:
                    StretchAnchors(ref _config.UiPosition.EventTileHelitiersMax, x, y);
                    return;
            }
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
            _chinookActive = false;
            _airdropActive = false;

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
                else if (!_airdropActive && entity is CargoPlane)
{
    _airdropActive = true;
}
                else if (!_chinookActive && entity is CH47HelicopterAIController)
                {
                    _chinookActive = true;
                }
				}
        }

private string GetPng(string key)
{
    if (ImageLibrary == null || string.IsNullOrWhiteSpace(key))
    {
        return null;
    }

    try
    {
        var result = ImageLibrary.Call("GetImage", key);
        return result as string;
    }
    catch
    {
        return null;
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
		
		private void AddMainBackground(CuiElementContainer container)
{
    if (container == null)
    {
        return;
    }

    var png = GetPng(_config.UiImages.MainBackgroundPng);
    if (!string.IsNullOrWhiteSpace(png))
    {
        container.Add(new CuiElement
        {
            Name = UiMainBackground,
            Parent = "Hud",
            Components =
            {
                new CuiRawImageComponent
                {
                    Png = png,
                    Color = "1 1 1 1"
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = _config.UiPosition.BackgroundAnchorMin,
                    AnchorMax = _config.UiPosition.BackgroundAnchorMax,
                    OffsetMin = _config.UiPosition.BackgroundOffsetMin,
                    OffsetMax = _config.UiPosition.BackgroundOffsetMax
                }
            }
        });
        return;
    }

    if (!string.IsNullOrWhiteSpace(_config.UiImages.MainBackgroundUrl))
    {
        container.Add(new CuiElement
        {
            Name = UiMainBackground,
            Parent = "Hud",
            Components =
            {
                new CuiRawImageComponent
                {
                    Url = _config.UiImages.MainBackgroundUrl,
                    Color = "1 1 1 1"
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = _config.UiPosition.BackgroundAnchorMin,
                    AnchorMax = _config.UiPosition.BackgroundAnchorMax,
                    OffsetMin = _config.UiPosition.BackgroundOffsetMin,
                    OffsetMax = _config.UiPosition.BackgroundOffsetMax
                }
            }
        });
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
			AddMainBackground(container);
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

            AddEventTiles(container);

            CuiHelper.AddUi(player, container);
        }

        private void AddEventTiles(CuiElementContainer container)
        {
            if (container == null)
            {
                return;
            }

            AddEventTile(container, "HELLTRAIN", IsCustomEventActive("HELLTRAIN"), _config.UiPosition.EventTileHelltrainMin, _config.UiPosition.EventTileHelltrainMax, "Helltrain");
            AddEventTile(container, "CONVOY", IsCustomEventActive("CONVOY"), _config.UiPosition.EventTileConvoyMin, _config.UiPosition.EventTileConvoyMax, "Convoy");
            AddEventTile(container, "CARGO", _cargoShipActive, _config.UiPosition.EventTileCargoMin, _config.UiPosition.EventTileCargoMax, "Cargo");
            AddEventTile(container, "AIRDROP", _airdropActive, _config.UiPosition.EventTileAirdropMin, _config.UiPosition.EventTileAirdropMax, "Airdrop");
            AddEventTile(container, "CHINOOK", _chinookActive, _config.UiPosition.EventTileChinookMin, _config.UiPosition.EventTileChinookMax, "Chinook");
            AddEventTile(container, "HELITIERS", _patrolHelicopterActive, _config.UiPosition.EventTileHelitiersMin, _config.UiPosition.EventTileHelitiersMax, "Helitiers");
        }

        private void AddEventTile(CuiElementContainer container, string label, bool isActive, string anchorMin, string anchorMax, string elementKey)
        {
            var panel = container.Add(new CuiPanel
            {
                Image = { Color = isActive ? _config.UiColors.EventActiveColor : _config.UiColors.EventInactiveColor },
                RectTransform =
                {
                    AnchorMin = anchorMin,
                    AnchorMax = anchorMax
                }
            }, UiMain, UiMain + ".Tile." + elementKey);

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = label,
                    FontSize = 10,
                    Align = TextAnchor.MiddleCenter,
                    Color = _config.UiColors.EventTextColor
                },
                RectTransform =
                {
                    AnchorMin = "0 0",
                    AnchorMax = "1 1"
                }
            }, panel);
        }

        private bool IsCustomEventActive(string eventName)
        {
            bool active;
            return _customEvents.TryGetValue(eventName, out active) && active;
        }

        private string BuildOnlineText()
        {
            return "ОНЛ: " + BasePlayer.activePlayerList.Count;
        }

        private string BuildMoscowTimeText()
        {
            return "МСК: " + DateTime.UtcNow.AddHours(3).ToString("HH:mm");
        }

        private string BuildServerTimeText()
        {
            var cycle = TOD_Sky.Instance?.Cycle;
            if (cycle == null)
            {
                return "Сервер: --:--";
            }

            return "СРВ: " + cycle.DateTime.ToString("HH:mm");
        }

        private void CreateEditorOverlay(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            DestroyEditorUi(player);

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

        private void UpdateEditorOverlayStatus(BasePlayer player, EditorState state)
        {
            if (player == null || state == null || !state.IsOpen)
            {
                return;
            }

            CuiHelper.DestroyUi(player, UiEditorStatus);

            var targetName = GetTargetName(state.TargetIndex);
            var targetMin = GetTargetMin(state.TargetIndex);
            var targetMax = GetTargetMax(state.TargetIndex);

            var container = new CuiElementContainer();
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
            }, UiEditor, UiEditorStatus);

            CuiHelper.AddUi(player, container);
        }

        private string GetTargetName(int targetIndex)
        {
            switch (targetIndex)
            {
                case 0: return "Button";
                case 1: return "InfoPanel";
                case 2: return "HelltrainTile";
                case 3: return "ConvoyTile";
                case 4: return "CargoTile";
                case 5: return "AirdropTile";
                case 6: return "ChinookTile";
                case 7: return "HelitiersTile";
                default: return "Button";
            }
        }

        private string GetTargetMin(int targetIndex)
        {
            switch (targetIndex)
            {
                case 0: return _config.UiPosition.ButtonAnchorMin;
                case 1: return _config.UiPosition.InfoAnchorMin;
                case 2: return _config.UiPosition.EventTileHelltrainMin;
                case 3: return _config.UiPosition.EventTileConvoyMin;
                case 4: return _config.UiPosition.EventTileCargoMin;
                case 5: return _config.UiPosition.EventTileAirdropMin;
                case 6: return _config.UiPosition.EventTileChinookMin;
                case 7: return _config.UiPosition.EventTileHelitiersMin;
                default: return _config.UiPosition.ButtonAnchorMin;
            }
        }

        private string GetTargetMax(int targetIndex)
        {
            switch (targetIndex)
            {
                case 0: return _config.UiPosition.ButtonAnchorMax;
                case 1: return _config.UiPosition.InfoAnchorMax;
                case 2: return _config.UiPosition.EventTileHelltrainMax;
                case 3: return _config.UiPosition.EventTileConvoyMax;
                case 4: return _config.UiPosition.EventTileCargoMax;
                case 5: return _config.UiPosition.EventTileAirdropMax;
                case 6: return _config.UiPosition.EventTileChinookMax;
                case 7: return _config.UiPosition.EventTileHelitiersMax;
                default: return _config.UiPosition.ButtonAnchorMax;
            }
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
			CuiHelper.DestroyUi(player, UiMainBackground);
        }

        private void DestroyEditorUi(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            CuiHelper.DestroyUi(player, UiEditorStatus);
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
