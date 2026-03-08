using Newtonsoft.Json;
using Oxide.Core.Libraries;
using UnityEngine;

namespace Oxide.Plugins
{
	[Info("NConnectMessages", "North", "1.0.3")]
	class NConnectMessages : RustPlugin
	{
		private const string Permission = "nconnectmessages.admin";

		private ConfigData cfg;
		private void Loaded()
		{
			Puts("Плагин успешно загружен. ");
			ReadConfig();
			if (!permission.PermissionExists(Permission)) permission.RegisterPermission(Permission, this);
		}

		void OnPlayerConnected(BasePlayer player)
		{
			ulong id = player.userID;
			string name = player.displayName;

			if (cfg.SendChatMSGOnPlayerConnected == true) 
			{
				if (!permission.UserHasPermission(player.UserIDString, Permission))
				{
					string connectMessage = GetConnectMessageForPlayer(player);
					Server.Broadcast($"<color={cfg.ColorNicknameMSG}>{name}</color> " + $"{connectMessage}", id);
				}
			}
		}

		private string GetConnectMessageForPlayer(BasePlayer player)
		{
			if (permission.UserHasGroup(player.UserIDString, cfg.WarlockGroupName)) return cfg.MSGOnPlayerConnectedWarlock;
			if (permission.UserHasGroup(player.UserIDString, cfg.LuciferGroupName)) return cfg.MSGOnPlayerConnectedLucifer;
			if (permission.UserHasGroup(player.UserIDString, cfg.DaemonGroupName)) return cfg.MSGOnPlayerConnectedDaemon;

			return cfg.MSGOnPlayerConnected;
		}

		void OnPlayerDisconnected(BasePlayer player)
		{
			ulong id = player.userID;
			string name = player.displayName;

			if (cfg.SendChatMSGOnPlayerDisconnected == true)
			{
				if (!permission.UserHasPermission(player.UserIDString, Permission)) Server.Broadcast($"<color={cfg.ColorNicknameMSG}>{name}</color> " + $"{cfg.MSGOnPlayerDisconnected}", id);
			}
		}

		#region [ CONFIG ]

		class ConfigData
		{
			[JsonProperty("Отображать сообщение о подключении игрока? ( true - Да, false - Нет ): ")]
			public bool SendChatMSGOnPlayerConnected = true;
			[JsonProperty("Отображать сообщение о отключении игрока? ( true - Да, false - Нет ): ")]
			public bool SendChatMSGOnPlayerDisconnected = true;
			[JsonProperty("Цвет никнейма игрока в сообщение ( HEX ): ")]
			public string ColorNicknameMSG = "#0384fc";
			[JsonProperty("Текст в сообщении когда игроки присоединился к серверу: ")]
			public string MSGOnPlayerConnected = "присоединился к игре.";
			[JsonProperty("Название группы WARLOCK для отдельного текста подключения: ")]
			public string WarlockGroupName = "warlock";
			[JsonProperty("Текст подключения для группы WARLOCK: ")]
			public string MSGOnPlayerConnectedWarlock = "присоединился к игре. [WARLOCK]";
			[JsonProperty("Название группы LUCIFER для отдельного текста подключения: ")]
			public string LuciferGroupName = "lucifer";
			[JsonProperty("Текст подключения для группы LUCIFER: ")]
			public string MSGOnPlayerConnectedLucifer = "присоединился к игре. [LUCIFER]";
			[JsonProperty("Название группы DAEMON для отдельного текста подключения: ")]
			public string DaemonGroupName = "daemon";
			[JsonProperty("Текст подключения для группы DAEMON: ")]
			public string MSGOnPlayerConnectedDaemon = "присоединился к игре. [DAEMON]";
			[JsonProperty("Текст в сообщении когда игроки вышел с сервера: ")]
			public string MSGOnPlayerDisconnected = "покинул сервер. ";

		}
		protected override void LoadDefaultConfig()
		{
			var config = new ConfigData();
			SaveConfig(config);
		}
		void SaveConfig(object config)
		{
			Config.WriteObject(config, true);
		}
		void ReadConfig()
		{
			base.Config.Settings.ObjectCreationHandling = ObjectCreationHandling.Replace;
			cfg = Config.ReadObject<ConfigData>();
			SaveConfig(cfg);
		}

		#endregion

	}
}
