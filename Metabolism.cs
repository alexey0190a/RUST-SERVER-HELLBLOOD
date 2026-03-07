using System.Collections.Generic;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Metabolism", "The Friendly Chap", "1.0.2")]
    [Description("Modify or disable player metabolism stats")]
    public class Metabolism : RustPlugin
	
/*	MIT License

	©2024 The Friendly Chap

	Permission is hereby granted, free of charge, to any person obtaining a copy
	of this software and associated documentation files (the "Software"), to deal
	in the Software without restriction, including without limitation the rights
	to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
	copies of the Software, and to permit persons to whom the Software is
	furnished to do so, subject to the following conditions:

	The above copyright notice and this permission notice shall be included in all
	copies or substantial portions of the Software.

	THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
	IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
	FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
	AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
	LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
	OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
	SOFTWARE.
*/	
	
    {
        #region Oxide Hooks
        
        private void Init()
        {
            foreach (var value in config.permissions.Keys)
            {
                permission.RegisterPermission(value, this);
            }
			// ShowLogo();
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            foreach (var pair in config.permissions)
            {
                if (permission.UserHasPermission(player.UserIDString, pair.Key))
                {
                    var data = pair.Value;

                    var health = data.health;
                    if (health > 100f)
                    {
                        player._maxHealth = health;
                    }

                    var hydration = data.hydration;
                    if (hydration > 250)
                    {
                        player.metabolism.hydration.max = hydration;
                    }

                    var calories = data.calories;
                    if (calories > 500)
                    {
                        player.metabolism.calories.max = calories;
                    }
                    
                    player.health = health;
                    player.metabolism.hydration.value = hydration;
                    player.metabolism.calories.value = calories;
                    player.SendNetworkUpdate();
                    break;
                }
            }
        }

        #endregion
        
        #region Configuration
        
        private static ConfigData config;
        
        private class ConfigData
        {
            [JsonProperty(PropertyName = "Permission -> Settings")]
            public Dictionary<string, MetabolismSettings> permissions;
        }

        private class MetabolismSettings
        {
            [JsonProperty(PropertyName = "Water on respawn")]
            public float hydration;
            
            [JsonProperty(PropertyName = "Calories on respawn")]
            public float calories;
            
            [JsonProperty(PropertyName = "Health on respawn")]
            public float health;
        }
        
        private ConfigData GetDefaultConfig()
        {
            return new ConfigData 
            {
                permissions = new Dictionary<string, MetabolismSettings>
                {
                    ["metabolism.3"] = new MetabolismSettings
                    {
                        hydration = 5000,
                        calories = 5000,
                        health = 100
                    },
                    ["metabolism.2"] = new MetabolismSettings
                    {
                        hydration = 500,
                        calories = 500,
                        health = 100
                    },
                    ["metabolism.1"] = new MetabolismSettings
                    {
                        hydration = 250,
                        calories = 250,
                        health = 100
                    },
                }
            };
        }
        
        protected override void LoadConfig()
        {
            base.LoadConfig();
   
            try
            {
                config = Config.ReadObject<ConfigData>();
        
                if (config == null)
                {
                    LoadDefaultConfig();
                }
            }
            catch
            {
                LoadDefaultConfig();
            }

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            PrintError("Configuration file is corrupt(or not exists), creating new one!");
            config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }
        
        #endregion
    		private void ShowLogo()
        {
			Puts(" _______ __               _______        __                 __ __             ______ __           ©2024");
			Puts("|_     _|  |--.-----.    |    ___|.----.|__|.-----.-----.--|  |  |.--.--.    |      |  |--.---.-.-----.");
			Puts("  |   | |     |  -__|    |    ___||   _||  ||  -__|     |  _  |  ||  |  |    |   ---|     |  _  |  _  |");
			Puts("  |___| |__|__|_____|    |___|    |__|  |__||_____|__|__|_____|__||___  |    |______|__|__|___._|   __|");
			Puts("                                Metabolism v1.0.1                 |_____| thefriendlychap.co.za |__|");      
        }
	}
}