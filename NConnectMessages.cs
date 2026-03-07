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
				if (!permission.UserHasPermission(player.UserIDString, Permission)) Server.Broadcast($"<color={cfg.ColorNicknameMSG}>{name}</color> " + $"{cfg.MSGOnPlayerConnected}", id);
			}
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