using System.IO;
using UnityEngine.Networking;
using Layer = Rust.Layer;
using System.Text;
using System;
using Oxide.Core;
using System.Linq;
using Physics = UnityEngine.Physics;
using Oxide.Core.Plugins;
using Newtonsoft.Json;
using Object = System.Object;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Oxide.Game.Rust.Cui;

namespace Oxide.Plugins
{
    [Info("IQRecycler", "rustmods.ru", "1.10.57")]
    [Description("IQRecycler")]
    public class IQRecycler : RustPlugin
    {
        
        private void DrawUI_ItemRequiredEmpty(BasePlayer player, Int32 X)
        {
            if (_interface == null) return;

            String Interface = InterfaceBuilder.GetInterface("UI_Crafting_Item_Required_Empty");
            if (Interface == null) return;
            
            Interface = Interface.Replace("%OFFSET_MIN%", $"{-190.8 + (X * 135)}");
            Interface = Interface.Replace("%OFFSET_MAX%", $"{-64.13+ (X * 135)}");
		   		 		  						  	   		  	   		  						  						  	 	 
            CuiHelper.AddUi(player, Interface);
        }    
                
                
        public UInt64[] GetFriendList(BasePlayer targetPlayer)
        {
            List<UInt64> FriendList = new List<UInt64>();
            if (Friends)
            {
                UInt64[] frinedList = (UInt64[])Friends?.Call("GetFriends", targetPlayer.userID);
                if (frinedList != null)
                    FriendList.AddRange(frinedList);
            }
            
            if (Clans)
            {
                UInt64[] ClanMembers = (UInt64[])Clans?.Call("GetClanMembers", targetPlayer.UserIDString);
                if (ClanMembers != null)
                    FriendList.AddRange(ClanMembers);
            }

            if(targetPlayer.Team != null)
                FriendList.AddRange(targetPlayer.Team.members);

            return FriendList.ToArray();
        }
        
        private String HasCrafting(BasePlayer player)
        {
            if (config.CraftingRecyclers.ItemCraftings.Count != 0)
            {
                Int32 ItemRequired = 0;

                foreach (Configuration.ItemPreset Item in config.CraftingRecyclers.ItemCraftings)
                {
                    if (!HasRequiredItems(player, Item.Shortname, Item.Amount, Item.SkinID))
                        ItemRequired++;
                }

                if (ItemRequired != 0)
                    return GetLang("UI_CRAFT_PANEL_BUTTON_NOTHING_RESOURCE", player.UserIDString);
            }

            if (!IsUsedEconomics) return String.Empty;
            return !permission.UserHasPermission(player.UserIDString, PermissionIgnorePayment) && !IsRemovedBalance(player.userID, config.CraftingRecyclers.PriceCrafting) ? GetLang("UI_CRAFT_PANEL_BUTTON_NOTHING_BALANCE", player.UserIDString) : String.Empty;
        }
        private static Double CurrentTime => Facepunch.Math.Epoch.Current;
        
        
                
        
        private static StringBuilder sb = new StringBuilder();
        private Boolean IsRecyclerSkinID(UInt64 SkinID) => config.ItemSetting.SkinID == SkinID;
        private Boolean IsValidRecycler(Recycler recycler) => RecyclerRepository.ContainsKey(recycler.OwnerID) && RecyclerRepository[recycler.OwnerID].ContainsKey(recycler.net.ID.Value);
        private Item API_GetItemRecycler(BasePlayer player) => config.ItemSetting.GetRecyclerItem(player);

        private void RemoveBalance(UInt64 userID, Int32 Balance)
        {
            if (IQEconomic != null)
                IQEconomic?.Call("API_REMOVE_BALANCE", userID, Balance);
            else if (Economics != null)
                Economics?.Call("Withdraw", userID, Convert.ToDouble(Balance));
            else if (ServerRewards != null)
                ServerRewards?.Call("TakePoints", userID, Balance);
        }
        
        private static IQRecycler _;

        private const String PermissionCrafting = "iqrecycler.craft";
        
        private void DrawUI_ProgressBar(BasePlayer player, Single Progress)
        {
            if (_interface == null) return;

            String Interface = InterfaceBuilder.GetInterface("UI_PickUp_Panel_ProgressBar");
            if (Interface == null) return;

            Interface = Interface.Replace("%X%", $"{Progress / config.SettingsRecycler.PickUpControllerRecycler.PickUpSeconds}");

            CuiHelper.AddUi(player, Interface);
        }

        
                
        private void DrawUI_StaticHealth(BasePlayer player)
        {
            if (_interface == null) return;
            InterfaceBuilder.DestroyHealth(player);
            
            String Interface = InterfaceBuilder.GetInterface("UI_Health_Panel");
            if (Interface == null) return;

            CuiHelper.AddUi(player, Interface);
        }    

        [ConsoleCommand("craft.recycler")]
        void ConsoleFuncCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;

            if (IsLimitUser(player))
            {
                SendChat(GetLang("ALERT_MESSAGE_LIMIT_USER", player.UserIDString), player);
                return;
            }

            CraftRecycler(player);
        }

        object CanLootEntity(BasePlayer player, Recycler recycler)
        {
            if (recycler != null && IsValidRecycler(recycler))
            {
                if (player.GetActiveItem() == null || !player.GetActiveItem().info.shortname.Equals("hammer"))
                {
                    if (config.SettingsRecycler.BlockRaidBlockUsed)
                        if (IsRaidBlocked(player))
                        {
                            SendChat(GetLang("ALERT_MESSAGE_USE_NO_RAIDBLOCK", player.UserIDString), player);
                            return false;
                        }

                    BlackListShowOrHide(player, recycler.OwnerID, true);

                    if (!playersOpenedRecycler.ContainsKey(player))
                        playersOpenedRecycler.Add(player, recycler);
                    return null;
                }

                if (config.SettingsRecycler.PickUpControllerRecycler.OnlyOwnerPickUP)
                {
                    if (player.userID != recycler.OwnerID)
                    {
                        UInt64[] FriendList = GetFriendList(player);
                        if (FriendList == null) return null;
                        if (!FriendList.Contains(recycler.OwnerID)) return null;
                    }
                }

                return false;
            }
		   		 		  						  	   		  	   		  						  						  	 	 
            return null;
        }

        
        
        private class RecyclerController : MonoBehaviour
        {
            private Recycler recycler;
            private BoxCollider boxCollder;
            
            private Dictionary<BasePlayer, RepositoryPickUp> playerList = new Dictionary<BasePlayer, RepositoryPickUp>();

            internal class RepositoryPickUp
            {
                public Single ProcessPickUP;
                public Boolean DrawedUIPickUp;
                public Boolean DrawedUIHealth;
                public UInt64[] TeamList;
            }
            
                        
            void Awake()
            {
                gameObject.layer = (Int32)Layer.Reserved1; 
                recycler = GetComponent<Recycler>();
                UpdateCollider();

                gameObject.AddComponent<DestroyOnGroundMissing>();
                gameObject.AddComponent<GroundWatch>();
                
                gameObject.SetActive(true);
                enabled = true;

                InvokeRepeating(nameof(PlayerInput), 0f, 0.1f);

                RefreshTriggersAfterInit();
            }

            private void RefreshTriggersAfterInit()
            {
                Collider[] colliders = Physics.OverlapSphere(recycler.transform.position, 1f);
                if (colliders == null) return;
                
                foreach (Collider collider in colliders)
                {
                    BasePlayer player = collider.GetComponent<BasePlayer>();
                    if (player != null)
                    {
                        if (config.SettingsRecycler.PickUpControllerRecycler.NoPickUpRaidBlock)
                            if (_.IsRaidBlocked(player)) return;

                        playerList.Add(player, new RepositoryPickUp()
                        {
                            DrawedUIPickUp = false,
                            DrawedUIHealth = false,
                            ProcessPickUP = 0,
                        });

                        if (!config.SettingsRecycler.PickUpControllerRecycler.OnlyOwnerPickUP) return;
                        if (player.Team == null) return;
                        playerList[player].TeamList = _.GetFriendList(player);
                    }
                }
            }

            private void OnTriggerEnter(Collider collider) 
            {
                BaseEntity baseEntity = collider.ToBaseEntity();
                if (!baseEntity.IsValid())
                    return;

                BasePlayer player = baseEntity as BasePlayer;
                if (player == null || !player.userID.IsSteamId() || playerList.ContainsKey(player)) return;
		   		 		  						  	   		  	   		  						  						  	 	 
                if (config.SettingsRecycler.PickUpControllerRecycler.NoPickUpRaidBlock)
                    if (_.IsRaidBlocked(player)) return;

                playerList.Add(player, new RepositoryPickUp()
                {
                    DrawedUIPickUp = false,
                    DrawedUIHealth = false,
                    ProcessPickUP = 0,
                });

                if (!config.SettingsRecycler.PickUpControllerRecycler.OnlyOwnerPickUP) return;
                if (player.Team == null) return;
                playerList[player].TeamList = _.GetFriendList(player);
            }

            private void OnTriggerExit(Collider collider) 
            {
                BasePlayer player = collider.GetComponentInParent<BasePlayer>();
                if (player != null && player.userID.IsSteamId() && playerList.ContainsKey(player))
                {
                    playerList.Remove(player);
                    InterfaceBuilder.DestroyPickUp(player);
                    InterfaceBuilder.DestroyHealth(player);
                }
            }
		   		 		  						  	   		  	   		  						  						  	 	 
            public void Kill()
            {
                foreach (KeyValuePair<BasePlayer, RepositoryPickUp> pList in playerList)
                {
                    InterfaceBuilder.DestroyPickUp(pList.Key);
                    InterfaceBuilder.DestroyHealth(pList.Key);
                }

                enabled = false;
                Destroy(this);
            }
            
            private void OnDestroy()
            {
                foreach (KeyValuePair<BasePlayer, RepositoryPickUp> pList in playerList)
                {
                    InterfaceBuilder.DestroyPickUp(pList.Key);
                    InterfaceBuilder.DestroyHealth(pList.Key);
                }
                Destroy(this);
            }
            
            
            
            private void PlayerInput()
            {
                UInt64 debugID = 0;
		   		 		  						  	   		  	   		  						  						  	 	 
                try
                {
                    if (playerList == null || playerList.Count == 0) return;

                    foreach (KeyValuePair<BasePlayer, RepositoryPickUp> repository in playerList)
                    {
                        debugID = repository.Key.userID;
                        if (config.SettingsRecycler.PickUpControllerRecycler.OnlyOwnerPickUP)
                        {
                            if (repository.Key.userID != recycler.OwnerID)
                            {
                                if (repository.Value.TeamList == null) continue;
                                if (!repository.Value.TeamList.Contains(recycler.OwnerID)) continue;
                            }
                        }

                        if (config.SettingsRecycler.PickUpControllerRecycler.NoPickUpBuildingBlock)
                            if (repository.Key != null &&
                                (!repository.Key.IsBuildingAuthed() || repository.Key.IsBuildingBlocked()))
                                continue;

                        if (repository.Key != null && repository.Key.eyes != null && recycler.IsVisible(repository.Key.eyes.HeadRay(), (Int32)Layer.Deployed, 1f))
                        {
                            InputState inputStateTrigger = repository.Key.serverInput;
                            Item activeItem = repository.Key.GetActiveItem();

                            if (inputStateTrigger.IsDown(BUTTON.USE) &&
                                (activeItem != null && activeItem.info.shortname.Equals("hammer")))
                            {
                                PickUpProcess(repository.Key);
                                playerList[repository.Key].ProcessPickUP += 0.1f;

                                if (playerList[repository.Key].DrawedUIHealth)
                                {
                                    playerList[repository.Key].DrawedUIHealth = false;
                                    InterfaceBuilder.DestroyHealth(repository.Key);
                                }
                            }
                            else if (playerList[repository.Key].ProcessPickUP != 0)
                            {
                                playerList[repository.Key].ProcessPickUP = 0;
                                playerList[repository.Key].DrawedUIPickUp = false;

                                InterfaceBuilder.DestroyPickUp(repository.Key);
                            }
                            else if (!playerList[repository.Key].DrawedUIPickUp)
                            {
                                if (activeItem != null && activeItem.info.shortname.Equals("hammer"))
                                    ShowHealth(repository.Key);
                                else if (_.GetHealthRecycler(recycler) <
                                         (config.SettingsRecycler.HealthCount * 0.75f))
                                    ShowHealth(repository.Key);
                                else if (playerList[repository.Key].DrawedUIHealth)
                                {
                                    playerList[repository.Key].DrawedUIHealth = false;
                                    InterfaceBuilder.DestroyHealth(repository.Key);
                                }
                            }
                        }
                        else if (playerList[repository.Key].ProcessPickUP != 0)
                        {
                            playerList[repository.Key].ProcessPickUP = 0;
                            playerList[repository.Key].DrawedUIPickUp = false;
                            InterfaceBuilder.DestroyPickUp(repository.Key);
                        }
                        else if (playerList[repository.Key].DrawedUIHealth)
                        {
                            playerList[repository.Key].DrawedUIHealth = false;
                            InterfaceBuilder.DestroyHealth(repository.Key);
                        }
                    }
                }
                catch (Exception e)
                {
                    String DebugLogs = $"Что-то есть...\n{e.Message}\n{e.StackTrace}\nБольше информации:\nrecycler.ownerid = {recycler.OwnerID}\nplayerID = {debugID}";
                    _.PrintError(DebugLogs);
                }
            }

            
            private void ShowHealth(BasePlayer player)
            {
                if (!playerList[player].DrawedUIHealth)
                {
                    _.DrawUI_StaticHealth(player);
                    _.DrawUI_Progress_Health(player, _.GetHealthRecycler(recycler));
                    playerList[player].DrawedUIHealth = true;
                }
		   		 		  						  	   		  	   		  						  						  	 	 
                if (_.IsLastDamage(recycler, config.SettingsRecycler.RepairControllerRecycler.SecondLastDamage - 0.05))
                    _.DrawUI_Progress_Health(player, _.GetHealthRecycler(recycler));
            }

            
            private void PickUpProcess(BasePlayer player)
            {
                if (!playerList[player].DrawedUIPickUp)
                {
                    _.DrawUI_StaticPickUP(player);
                    playerList[player].DrawedUIPickUp = true;
                }
		   		 		  						  	   		  	   		  						  						  	 	 
                if (playerList[player].ProcessPickUP < config.SettingsRecycler.PickUpControllerRecycler.PickUpSeconds)
                    _.DrawUI_ProgressBar(player, playerList[player].ProcessPickUP);
                else
                {
                    Single ConditinRecycler = _.GetResultCondition(recycler);

                    _.NextTick(() =>
                    {
                        playerList[player].DrawedUIPickUp = false;
                        InterfaceBuilder.DestroyPickUp(player);
                        recycler.Kill();

                        Item Recycler = config.ItemSetting.GetRecyclerItem(player, 1, ConditinRecycler);
                        player.GiveItem(Recycler);
                    });
                }
            }

            
            private void UpdateCollider()
            {
                boxCollder = gameObject.GetComponent<BoxCollider>();
                {
                    if (boxCollder == null)
                    {
                        boxCollder = gameObject.AddComponent<BoxCollider>();
                        boxCollder.isTrigger = true;
                        boxCollder.name = "RECYCLER_COLLIDER";
                    }
                    boxCollder.size = new Vector3(4f, 4f, 4f);
                }
            }

                    }
        private class Configuration
        {
            
            internal class ItemRecycler
            {
                [JsonProperty(LanguageEn ? "The recycler's display name (in the inventory)" : "Отображаемое имя переработчика (в инвентаре)")]
                public LanguageTitles DisplayName;
                [JsonProperty(LanguageEn ? "SkinID for the image (required field)" : "SkinID для картинки (обязательное поле)")]
                public UInt64 SkinID;

                public Item GetRecyclerItem(BasePlayer player, Int32 Amount = 1, Single Codition = 1f)
                {
                    Item Recycler = ItemManager.CreateByName(ReplaceItem, Amount, SkinID);
                    Recycler.name = DisplayName.GetLanguageText(player);
                    Recycler.condition = Codition <= 0.01f ? -1 : Codition;
                    return Recycler;
                }
            }
            
            internal class ItemPreset
            {
                public String Shortname;
                public Int32 Amount;
                public UInt64 SkinID;
                [JsonProperty(LanguageEn ? "Item names for display to the player" : "Названия предметов для отображения игроку")]
                public LanguageTitles TitleItems;
            }
            [JsonProperty(LanguageEn ? "List of recycling settings by privileges [Permission] = Setting" : "Список настроек переработки по привилегиям [Права] = Настройка")]
            public Dictionary<String, PresetRecycle> PresetsRecycle = new Dictionary<String, PresetRecycle>();

            internal class CraftingRecycler
            {
                [JsonProperty(LanguageEn ? "Use craft recycler (UI) (true - yes/false - no)" : "Использовать встроенный крафт переработчика (UI) (true - да/false - нет)")]
                public Boolean UseCraftRecycler;
                [JsonProperty(LanguageEn ? "List of items for the Recycler crafting recipe (can hold no more than 5 pieces)" : "Список предметов для крафта переработчика (вмещает не более 5 штук)")]
                public List<ItemPreset> ItemCraftings = new List<ItemPreset>();
                [JsonProperty(LanguageEn ? "Use charging for crafting the recycler (IQEconomic, Economics, ServerRewards) (true - yes/false - no)" : "Использовать взятие платы за крафт переработчика (IQEconomic, Economics, ServerRewards) (true - да/false - нет)")]
                public Boolean UseEconomics;
                [JsonProperty(LanguageEn ? "Crafting cost of the recycler (IQEconomic, Economics, ServerRewards)" : "Стоимость крафта переработчика (IQEconomic, Economics, ServerRewards)")]
                public Int32 PriceCrafting;

                [JsonProperty(LanguageEn ? "Refiner crafting limits setting" : "Настройка лимитов крафта переработчика")]
                public LimitCrafting limitCrafting;

                internal class LimitCrafting
                {
                    [JsonProperty(LanguageEn ? "Use limits on crafting a recycler (true - yes/false - no)" : "Использовать лимиты на крафт переработчика (true - да/false - нет)")]
                    public Boolean UseLimits;
                    [JsonProperty(LanguageEn ? "How many recyclers the player can craft" : "Сколько переработчиков игрок сможет скрафтить")]
                    public Int32 LimitAmount;
                }
            }
            [JsonProperty(LanguageEn ? "Setting up a recycler Item" : "Настройка предмета переработчика")]
            public ItemRecycler ItemSetting = new ItemRecycler();

            internal class PresetRecycle
            {
                [JsonProperty(LanguageEn ? "Replacement of dropped items after recycling" : "Настройка замены выпадающих предметов после переработки")]
                public ItemRecycled ItemRecycledController = new ItemRecycled();
                [JsonProperty(LanguageEn ? "Blacklist of items that cannot be recycled" : "Настройка черного списка предметов, которые нельзя переработать")]
                public BlackListRecycled BlackListsController = new BlackListRecycled();
                [JsonProperty(LanguageEn ? "Item recycling speed settings" : "Настройка скорости переработки предметов")]
                public SpeedRecycled SpeedRecycledController = new SpeedRecycled();
		   		 		  						  	   		  	   		  						  						  	 	 
                internal class ItemRecycled
                {

                    public List<Item> GetOutputItem(Item inputItems)
                    {
                        List<Item> outputItems = new List<Item>();

                        foreach (ItemInput InputItems in ItemInputs)
                        {
                            if (!InputItems.Shortname.Equals(inputItems.info.shortname) || InputItems.SkinID != inputItems.skin) continue;
                            if (InputItems.TitleItem != null && !String.IsNullOrWhiteSpace(InputItems.TitleItem) && !InputItems.TitleItem.Equals(inputItems.name)) continue;
                            
                            foreach (ItemInput.ItemOutput itemOutput in InputItems.OutputList)
                            {
                                if (!_.IsRare(itemOutput.RareDrop)) continue;
                                
                                Item outputItem = ItemManager.CreateByName(itemOutput.Shortname, itemOutput.Amount, itemOutput.SkinID);
                                if (!String.IsNullOrWhiteSpace(itemOutput.TitleItem))
                                    outputItem.name = itemOutput.TitleItem;
                                outputItems.Add(outputItem);
                            }
                        }

                        return outputItems;
                    }

                    internal class ItemInput
                    {
                        [JsonProperty(LanguageEn ? "Displayed item name (if necessary)" : "Отображаемое имя предмета (при необходимости)")]
                        public String TitleItem;
                        public List<ItemOutput> OutputList = new List<ItemOutput>();
                        public UInt64 SkinID;
                        public String Shortname;
                        internal class ItemOutput
                        {

                            [JsonProperty(LanguageEn ? "Amount per 1 unit of recycled item" : "Количество за 1 единицу переработанного предмета")]
                            public Int32 Amount;
                            public UInt64 SkinID;
                            [JsonProperty(LanguageEn ? "Item drop chance" : "Шанс выпадения предмета")]
                            public Int32 RareDrop;
                            public String Shortname;
                            [JsonProperty(LanguageEn ? "Displayed item name" : "Отображаемое имя предмета")]
                            public String TitleItem;
                        }
                    }
                    [JsonProperty(LanguageEn ? "Use item replacement after recycling function" : "Использовать функцию замены предметов после переработки")]
                    public Boolean UseItemRecycled;
                    [JsonProperty(LanguageEn ? "Item recycling settings, you can replace the dropped items after recycling [Incoming item (to be recycled) = List of items to be given after recycling]" : "Настройка переработки предметов, вы можете заменить выпадающие предметы после переработки [Входящий предмет (который будет переработан) = Список предметов, которые отдадутся после переработки]")]
                    public List<ItemInput> ItemInputs = new List<ItemInput>();
                }

                internal class BlackListRecycled
                {
                    [JsonProperty(LanguageEn ? "Use blacklist of items (true - yes/false - no)" : "Использовать черный список предметов (true - да/false - нет)")]
                    public Boolean UseBlackList;
                    [JsonProperty(LanguageEn ? "List of items that cannot be recycled" : "Черный список предметов, которые нельзя переработать")]
                    public List<ItemSetting> BlackItemList = new List<ItemSetting>();

                    
                    internal class ItemSetting
                    {
                        public String Shortname;
                        public UInt64 SkinID;
                    }

                    public Boolean IsBlackList(Item item)
                    {
                        foreach (ItemSetting itemSetting in BlackItemList)
                            if (item.info.shortname.Equals(itemSetting.Shortname) && item.skin == itemSetting.SkinID)
                                return true;

                        return false;
                    }
		   		 		  						  	   		  	   		  						  						  	 	 
                }

                internal class SpeedRecycled
                {
                    [JsonProperty(LanguageEn ? "Use recycling speed editing" : "Использовать редактирование скорости переработки")]
                    public Boolean UseSpeed;
                    [JsonProperty(LanguageEn ? "How many seconds the item recycling will last (default in RUST: 5 seconds)" : "Сколько секунд будет длиться переработка предмета (по стандарту в RUST: 5 секунд)")]
                    public Single RecycledSeconds;
                }
            }
            [JsonProperty(LanguageEn ? "Standard recycling process settings (available to all players)" : "Стандартная настройка процесса переработки (доступна всем игрокам)")]
            public PresetRecycle DefaultSettingRecycle = new PresetRecycle();

            internal class ReferencePlugins
            {
                [JsonProperty(LanguageEn ? "Settings IQChat" : "Настройка IQChat")]
                public IQChatSettings IQChatReference;
                internal class IQChatSettings
                {
                    [JsonProperty(LanguageEn ? "IQChat : Custom prefix in chat" : "IQChat : Кастомный префикс в чате")]
                    public String CustomPrefix;
                    [JsonProperty(LanguageEn ? "IQChat : Custom chat avatar (If required)" : "IQChat : Кастомный аватар в чате(Если требуется)")]
                    public String CustomAvatar;
                    [JsonProperty(LanguageEn ? "IQChat : Use UI notification (true - yes/false - no)" : "IQChat : Использовать UI уведомление (true - да/false - нет)")]
                    public Boolean UIAlertUse;
                }
            }
            [JsonProperty(LanguageEn ? "Settings supports plugins" : "Настройка поддерживаемых плагинов")]
            public ReferencePlugins ReferencePluginsSettings = new ReferencePlugins();
            [JsonProperty(LanguageEn ? "Recycler crafting configuration" : "Настройка крафта переработчика")]
            public CraftingRecycler CraftingRecyclers = new CraftingRecycler();
            
            internal class LanguageTitles
            {
                [JsonProperty(LanguageEn ? "Text in Russian" : "Текст на русском")]
                public String RussuianText;
                [JsonProperty(LanguageEn ? "Text in English" : "Текст на английском")]
                public String EnglishText;

                public String GetLanguageText(BasePlayer player) => _.lang.GetLanguage(player.UserIDString).Equals("ru") ? RussuianText : EnglishText;
            }

            public static Configuration GetNewConfiguration() 
            {
                return new Configuration
                {
                    DefaultSettingRecycle = new PresetRecycle()
                    {
                        SpeedRecycledController = new PresetRecycle.SpeedRecycled()
                        {
                            UseSpeed = true,
                            RecycledSeconds = 5f,
                        },
                        BlackListsController  =  new PresetRecycle.BlackListRecycled()
                        {
                            UseBlackList = true,
                            BlackItemList = new List<PresetRecycle.BlackListRecycled.ItemSetting>()
                            {
                                new PresetRecycle.BlackListRecycled.ItemSetting()
                                {
                                    Shortname = "gears",
                                    SkinID = 0,
                                },
                                new PresetRecycle.BlackListRecycled.ItemSetting()
                                {
                                    Shortname = "techparts",
                                    SkinID = 0,
                                },
                                new PresetRecycle.BlackListRecycled.ItemSetting()
                                {
                                    Shortname = "metalspring",
                                    SkinID = 0,
                                },
                            }
                        },
                        ItemRecycledController = new PresetRecycle.ItemRecycled()
                        {
                            UseItemRecycled = false,
                            ItemInputs = new List<PresetRecycle.ItemRecycled.ItemInput>()
                            {
                                new PresetRecycle.ItemRecycled.ItemInput()
                                {
                                    Shortname = "rifle.ak",
                                    SkinID = 0,
                                    
                                    OutputList = new List<PresetRecycle.ItemRecycled.ItemInput.ItemOutput>()
                                    {
                                        new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                        {
                                            Shortname = "metal.refined",
                                            Amount = 25,
                                            SkinID = 0,
                                            RareDrop = 100,
                                            TitleItem = ""
                                        },
                                        new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                        {
                                            Shortname = "wood",
                                            Amount = 100,
                                            SkinID = 0,
                                            RareDrop = 100,
                                            TitleItem = ""
                                        },
                                        new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                        {
                                            Shortname = "metalspring",
                                            Amount = 2,
                                            SkinID = 0,
                                            RareDrop = 50,
                                            TitleItem = ""
                                        },
                                        new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                        {
                                            Shortname = "riflebody",
                                            Amount = 1,
                                            SkinID = 0,
                                            RareDrop = 15,
                                            TitleItem = ""
                                        },
                                    }
                                },
                                new PresetRecycle.ItemRecycled.ItemInput()
                                {
                                    Shortname = "explosive.timed",
                                    SkinID = 0,
                                    
                                    OutputList = new List<PresetRecycle.ItemRecycled.ItemInput.ItemOutput>()
                                    {
                                        new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                        {
                                            Shortname = "explosives",
                                            Amount = 5,
                                            SkinID = 0,
                                            RareDrop = 100,
                                            TitleItem = ""
                                        },
                                        new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                        {
                                            Shortname = "cloth",
                                            Amount = 3,
                                            SkinID = 0,
                                            RareDrop = 100,
                                            TitleItem = ""
                                        },
                                        new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                        {
                                            Shortname = "wood",
                                            Amount = 100,
                                            SkinID = 0,
                                            RareDrop = 100,
                                            TitleItem = ""
                                        },
                                        new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                        {
                                            Shortname = "techparts",
                                            Amount = 1,
                                            SkinID = 0,
                                            RareDrop = 50,
                                            TitleItem = ""
                                        },
                                    }
                                },
                                new PresetRecycle.ItemRecycled.ItemInput()
                                {
                                    Shortname = "spraycan",
                                    SkinID = 0,
                                    
                                    OutputList = new List<PresetRecycle.ItemRecycled.ItemInput.ItemOutput>()
                                    {
                                        new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                        {
                                            Shortname = "bleach",
                                            Amount = 5,
                                            SkinID = 1337228,
                                            RareDrop = 100,
                                            TitleItem = LanguageEn ? "Coin" : "Монета удачи",
                                        },
                                    }
                                },
                            }
                        }
                    },
                    PresetsRecycle = new Dictionary<String, PresetRecycle>()
                    {
                        ["iqrecycler.premium"] = new PresetRecycle()
                        {
                            SpeedRecycledController = new PresetRecycle.SpeedRecycled()
                            {
                                UseSpeed = true,
                                RecycledSeconds = 2f,
                            },
                            BlackListsController = new PresetRecycle.BlackListRecycled() 
                            {
                                UseBlackList = false,
                                BlackItemList = new List<PresetRecycle.BlackListRecycled.ItemSetting>() { }
                            },
                            ItemRecycledController = new PresetRecycle.ItemRecycled()
                            {
                                UseItemRecycled = false,
                                ItemInputs = new List<PresetRecycle.ItemRecycled.ItemInput>()
                                {
                                    new PresetRecycle.ItemRecycled.ItemInput()
                                    {
                                        Shortname = "rifle.ak",
                                        SkinID = 0,
                                        
                                        OutputList = new List<PresetRecycle.ItemRecycled.ItemInput.ItemOutput>()
                                        {
                                            new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                            {
                                                Shortname = "metal.refined",
                                                Amount = 50,
                                                SkinID = 0,
                                                RareDrop = 80,
                                                TitleItem = ""
                                            },
                                            new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                            {
                                                Shortname = "wood",
                                                Amount = 200,
                                                SkinID = 0,
                                                RareDrop = 100,
                                                TitleItem = ""
                                            },
                                            new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                            {
                                                Shortname = "metalspring",
                                                Amount = 3,
                                                SkinID = 0,
                                                RareDrop = 50,
                                                TitleItem = ""
                                            },
                                            new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                            {
                                                Shortname = "riflebody",
                                                Amount = 2,
                                                SkinID = 0,
                                                RareDrop = 20,
                                                TitleItem = ""
                                            },
                                        }
                                    },
                                    new PresetRecycle.ItemRecycled.ItemInput()
                                    {
                                        Shortname = "explosive.timed",
                                        SkinID = 0,
                                        
                                        OutputList = new List<PresetRecycle.ItemRecycled.ItemInput.ItemOutput>()
                                        {
                                            new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                            {
                                                Shortname = "explosives",
                                                Amount = 6,
                                                SkinID = 0,
                                                RareDrop = 100,
                                                TitleItem = ""
                                            },
                                            new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                            {
                                                Shortname = "cloth",
                                                Amount = 10,
                                                SkinID = 0,
                                                RareDrop = 100,
                                                TitleItem = ""
                                            },
                                            new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                            {
                                                Shortname = "wood",
                                                Amount = 200,
                                                SkinID = 0,
                                                RareDrop = 100,
                                                TitleItem = ""
                                            },
                                            new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                            {
                                                Shortname = "techparts",
                                                Amount = 2,
                                                SkinID = 0,
                                                RareDrop = 50,
                                                TitleItem = ""
                                            },
                                        }
                                    },
                                }
                            }
                        },
                        ["iqrecycler.vip"] = new PresetRecycle()
                        {
                            SpeedRecycledController = new PresetRecycle.SpeedRecycled()
                            {
                                UseSpeed = true,
                                RecycledSeconds = 3.5f,
                            },
                            BlackListsController = new PresetRecycle.BlackListRecycled()
                            {
                                UseBlackList = true,
                                BlackItemList = new List<PresetRecycle.BlackListRecycled.ItemSetting>()
                                {
                                    new PresetRecycle.BlackListRecycled.ItemSetting()
                                    {
                                        Shortname = "techparts",
                                        SkinID = 0,
                                    },
                                }
                            },
                            ItemRecycledController = new PresetRecycle.ItemRecycled()
                            {
                                UseItemRecycled = false,
                                ItemInputs = new List<PresetRecycle.ItemRecycled.ItemInput>()
                                {
                                    new PresetRecycle.ItemRecycled.ItemInput()
                                    {
                                        Shortname = "rifle.ak",
                                        SkinID = 0,
                                        
                                        OutputList = new List<PresetRecycle.ItemRecycled.ItemInput.ItemOutput>()
                                        {
                                            new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                            {
                                                Shortname = "metal.refined",
                                                Amount = 50,
                                                SkinID = 0,
                                                RareDrop = 80,
                                                TitleItem = ""
                                            },
                                            new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                            {
                                                Shortname = "wood",
                                                Amount = 200,
                                                SkinID = 0,
                                                RareDrop = 100,
                                                TitleItem = ""
                                            },
                                            new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                            {
                                                Shortname = "metalspring",
                                                Amount = 3,
                                                SkinID = 0,
                                                RareDrop = 50,
                                                TitleItem = ""
                                            },
                                            new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                            {
                                                Shortname = "riflebody",
                                                Amount = 2,
                                                SkinID = 0,
                                                RareDrop = 20,
                                                TitleItem = ""
                                            },
                                        }
                                    },
                                    new PresetRecycle.ItemRecycled.ItemInput()
                                    {
                                        Shortname = "explosive.timed",
                                        SkinID = 0,
                                        
                                        OutputList = new List<PresetRecycle.ItemRecycled.ItemInput.ItemOutput>()
                                        {
                                            new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                            {
                                                Shortname = "explosives",
                                                Amount = 4,
                                                SkinID = 0,
                                                RareDrop = 100,
                                                TitleItem = ""
                                            },
                                            new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                            {
                                                Shortname = "cloth",
                                                Amount = 5,
                                                SkinID = 0,
                                                RareDrop = 100,
                                                TitleItem = ""
                                            },
                                            new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                            {
                                                Shortname = "wood",
                                                Amount = 150,
                                                SkinID = 0,
                                                RareDrop = 100,
                                                TitleItem = ""
                                            },
                                            new PresetRecycle.ItemRecycled.ItemInput.ItemOutput()
                                            {
                                                Shortname = "techparts",
                                                Amount = 1,
                                                SkinID = 0,
                                                RareDrop = 60,
                                                TitleItem = ""
                                            },
                                        }
                                    },
                                }
                            }
                        },
                    },
                    CraftingRecyclers = new CraftingRecycler()
                    {
                        UseCraftRecycler = true,
                        limitCrafting = new CraftingRecycler.LimitCrafting()
                        {
                            UseLimits = false,
                            LimitAmount = 5,
                        },
                        UseEconomics = false,
                        PriceCrafting = 100,
                        ItemCraftings = new List<ItemPreset>()
                        {
                            new ItemPreset
                            {
                                Shortname = "metal.fragments",
                                Amount = 500,
                                SkinID = 0,
                                TitleItems = new LanguageTitles()
                                {
                                    RussuianText = "фрагменты металл",
                                    EnglishText = "metal fragments"
                                },
                            },
                            new ItemPreset()
                            {
                                Shortname = "metal.refined",
                                Amount = 50,
                                SkinID = 0,
                                TitleItems = new LanguageTitles()
                                {
                                    RussuianText = "металл высокого качества",
                                    EnglishText = "metal refined",
                                }
                            },
                            new ItemPreset()
                            {
                                Shortname = "scrap",
                                Amount = 70,
                                SkinID = 0,
                                TitleItems = new LanguageTitles()
                                {
                                    RussuianText = "металлолом",
                                    EnglishText = "scrap",
                                }
                            },
                            new ItemPreset()
                            {
                                Shortname = "techparts",
                                Amount = 10,
                                SkinID = 0,
                                TitleItems = new LanguageTitles()
                                {
                                    RussuianText = "микросхемы",
                                    EnglishText = "tech trash",
                                }
                            },
                        }
                    },
                    ItemSetting = new ItemRecycler()
                    {
                        DisplayName = new LanguageTitles()
                        {
                            RussuianText = "Домашний переработчик",
                            EnglishText = "Home recycler",
                        },
                        SkinID = 2976924191,
                    },
                    SettingsRecycler = new SettingRecycler()
                    {
                        HealthCount = 750,
                        BlockRaidBlockUsed = false,
                        
                        InstallControllerRecycler = new SettingRecycler.InstallRecycler()
                        {
                            BlockBuildingBlock = true,
                            UseGround = false,
                            BlockRaidBlock = true,
                        },
                        PickUpControllerRecycler = new SettingRecycler.PickUpRecycler()
                        {
                            OnlyOwnerPickUP = false,
                            NoPickUpBuildingBlock = true,
                            NoPickUpRaidBlock = true,
                            PickUpSeconds = 12,
                            ConditionSettings = new SettingRecycler.PickUpRecycler.ConditionController()
                            {
                                UseConditionDamage = true,
                                PercentCondition = 25,
                            }
                        },
                        DamageControllerRecycler = new SettingRecycler.DamageController()
                        {
                            UseDamageRecycler = true,
                            CustomDamageList = new Dictionary<String, Single>
                            {
                                ["multiplegrenadelauncher"] = 40f,
                                ["grenade.beancan"] = 25f,
                                ["grenade.f1.deployed"] = 35f,
                                ["explosive.satchel"] = 130f,
                                ["explosive.timed"] = 300f,
                                ["ammo.rocket.basic"] = 190f,
                                ["ammo.rocket.hv"] = 140f,
                                ["ammo.rocket.fire"] = 80f,
                                ["surveycharge"] = 10f,
                            },
                        },
                        RepairControllerRecycler = new SettingRecycler.RepairRecycler()
                        {
                            UseReapir = true,
                            UseDamageController = true,
                            SecondLastDamage = 20f,
                            HealthAmount = 100,
                            RepairItems = new List<ItemPreset>()
                            {
                                new ItemPreset
                                {
                                    Shortname = "metal.fragments",
                                    Amount = 30,
                                    SkinID = 0,
                                    TitleItems = new LanguageTitles()
                                    {
                                        RussuianText = "фрагменты металла",
                                        EnglishText = "metal fragments",
                                    }
                                },
                                new ItemPreset
                                {
                                    Shortname = "metal.refined",
                                    Amount = 5,
                                    SkinID = 0,
                                    TitleItems = new LanguageTitles()
                                    {
                                        RussuianText = "металл высокого качества",
                                        EnglishText = "metal refined",
                                    }
                                }
                            }
                        }
                    },
                    ReferencePluginsSettings = new ReferencePlugins()
                    {
                        IQChatReference = new ReferencePlugins.IQChatSettings()
                        {
                            CustomAvatar = "0",
                            CustomPrefix = "[<color=#64D15D>IQRecycler</color>]\n",
                            UIAlertUse = false,
                        }
                    }
                };
            }

            internal class SettingRecycler
            {
                [JsonProperty(LanguageEn ? "Install recycler controllerr" : "Настройка установки переработчика")]
                public InstallRecycler InstallControllerRecycler = new InstallRecycler();
                [JsonProperty(LanguageEn ? "Pickup of a recycler" : "Настройка поднятия переработчика")]
                public PickUpRecycler PickUpControllerRecycler = new PickUpRecycler();
                [JsonProperty(LanguageEn ? "Health Count" : "Количество прочности переработчика (здоровье)")]
                public Int32 HealthCount;
                [JsonProperty(LanguageEn ? "Damage Controller Recycler" : "Настройка урона по переработчику")]
                public DamageController DamageControllerRecycler = new DamageController();
                [JsonProperty(LanguageEn ? "Recycler repair settings" : "Настройка починки переработчика")]
                public RepairRecycler RepairControllerRecycler = new RepairRecycler();

                [JsonProperty(LanguageEn ? "Disallow the use of a recycler during a raid block (true - yes/false - no). (true - yes/false - no)" : "Запретить использовать переработчик во время рейдблока (true - да/false - нет)")]
                public Boolean BlockRaidBlockUsed;
                
                internal class DamageController
                {
                    [JsonProperty(LanguageEn ? "Use Damage Recycler (true - yes/false - no)" : "Разрешить наносить урон переработчику (true - да/false - нет)")]
                    public Boolean UseDamageRecycler;
                    [JsonProperty(LanguageEn ? "Custom Damage List [Shortname] = Damage" : "Кастомная настройка наносимого урона определенным оружием  [Shortname] = Урон")]
                    public Dictionary<String, Single> CustomDamageList = new Dictionary<String, Single>();
                }
                
                internal class PickUpRecycler
                {
                    [JsonProperty(LanguageEn ? "How many seconds will a recycler pick up" : "Сколько секунд будет подниматься переработчик")]
                    public Int32 PickUpSeconds;
                    [JsonProperty(LanguageEn ? "You can pickup the recycler only in the area of the cupboard (building zone) (true - yes/false - no)" : "Поднять переработчик можно только в зоне действия шкафа (true - да/false - нет)")]
                    public Boolean NoPickUpBuildingBlock;
                    [JsonProperty(LanguageEn ? "Only the owner who installed it or his friends can pickup the recycler (true - yes/false - no)" : "Поднять переработчик может только владелец, который его установил или его друзья (true - да/false - нет)")]
                    public Boolean OnlyOwnerPickUP;
                    [JsonProperty(LanguageEn ? "Disable recycler pickup during raid block (true - yes/false - no)" : "Запретить подбор переработчика во время рейдблока (true - да/false - нет)")]
                    public Boolean NoPickUpRaidBlock;
                    
                    [JsonProperty(LanguageEn ? "Recycler condition setting" : "Настройка прочности при поднятии переработчика")]
                    public ConditionController ConditionSettings = new ConditionController();
                    internal class ConditionController
                    {
                        [JsonProperty(LanguageEn ? "Use condition removal from recycler when picking it up (true - yes/false - no)" : "Использовать снятие прочности у переработчика при его подборе (true - да/false - нет)")]
                        public Boolean UseConditionDamage;
                        [JsonProperty(LanguageEn ? "How much % of condition to remove from an object when it is raised (0 - 100)" : "Сколько % прочности снимать у предмета при его подъеме (0 - 100)")]
                        public Int32 PercentCondition;
                        public Int32 GetPercentCondition() => !UseConditionDamage ? 0 : PercentCondition > 100 ? 100 : PercentCondition;
                    }
                }
                
                internal class InstallRecycler
                {
                    [JsonProperty(LanguageEn ? "The recycler can be installed only in the area of the cupboard (building zone) (true - yes/false - no)" : "Установить переработчик можно только в зоне действия шкафа (true - да/false - нет)")]
                    public Boolean BlockBuildingBlock;
                    [JsonProperty(LanguageEn ? "The ability to install a recycler on the ground (true - yes/false - no)" : "Можно установить переработчик на землю (true - да/false - нет)")]
                    public Boolean UseGround;
                    [JsonProperty(LanguageEn ? "Prohibit installing a recycler during a raid block (true - yes/false - no)" : "Запретить устанавливать переработчик во время рейдблока (true - да/false - нет)")]
                    public Boolean BlockRaidBlock;
                }
                
                internal class RepairRecycler
                {
                    [JsonProperty(LanguageEn ? "Use the ability to repair the recycler (true - yes/false - no)" : "Использовать возможность починить переработчик (true - да/false - нет)")]
                    public Boolean UseReapir;
                    [JsonProperty(LanguageEn ? "Use prohibition on repairing the recycler if it has recently been damaged (true - yes/false - no) (damage support for the recycler must be enabled)" : "Использовать запрет на починку если переработчику недавно нанесли урон (true - да/false - нет) (у вас должна быть включена поддержка нанесения урона переработчику)")]
                    public Boolean UseDamageController;
                    [JsonProperty(LanguageEn ? "How many seconds until the recycler can be repaired after it has been damaged" : "Через сколько секунд можно будет чинить переработчик после нанесенного ему урона")]
                    public Double SecondLastDamage;
                    [JsonProperty(LanguageEn ? "How much durability will be replenished for the recycler per one repair" : "Сколько будет пополняться прочности у переработчика за одну починку")]
                    public Int32 HealthAmount;
                    [JsonProperty(LanguageEn ? "Items that will be taken during repairs" : "Предметы которые будут забираться во время починки")]
                    public List<ItemPreset> RepairItems = new List<ItemPreset>();
                }
            }
            [JsonProperty(LanguageEn ? "Settings Recycler" : "Настройка переработчика")]
            public SettingRecycler SettingsRecycler = new SettingRecycler();
        }
        private readonly Dictionary<String, String> ShortPrefabToShortname = new Dictionary<String, String>()
        {
            ["40mm_grenade_he"] = "multiplegrenadelauncher",
            ["grenade.beancan.deployed"] = "grenade.beancan",
            ["grenade.f1.deployed"] = "grenade.f1",
            ["explosive.satchel.deployed"] = "explosive.satchel",
            ["explosive.timed.deployed"] = "explosive.timed",
            ["rocket_basic"] = "ammo.rocket.basic",
            ["rocket_hv"] = "ammo.rocket.hv",
            ["rocket_fire"] = "ammo.rocket.fire",
            ["survey_charge.deployed"] = "surveycharge"
        };

        private Tuple<String, List<Configuration.ItemPreset>> HasRepair(BasePlayer player, Single Health, Single MaxHealth)
        {
            String RequiredItems = String.Empty;
            if (config.SettingsRecycler.RepairControllerRecycler.RepairItems.Count == 0) return new Tuple<String, List<Configuration.ItemPreset>>(RequiredItems, null);
            List<Configuration.ItemPreset> requiredItems = CalculateRepairCost(Health, MaxHealth, config.SettingsRecycler.RepairControllerRecycler.RepairItems);

            foreach (Configuration.ItemPreset Item in requiredItems)
            {
                if (!HasRequiredItems(player, Item.Shortname, Item.Amount, Item.SkinID))
                    RequiredItems += $"\n- {Item.TitleItems.GetLanguageText(player)} : <color=#CD412B>x{Item.Amount}</color>";
            }

            return new Tuple<String, List<Configuration.ItemPreset>>(RequiredItems, requiredItems);
        }
        
        private object CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            BasePlayer player = planner.GetOwnerPlayer();
            if (player == null) return null;

            Item ItemRecycler = player.GetActiveItem();
            if (ItemRecycler == null) return null;

            if (!IsRecyclerSkinID(ItemRecycler.skin)) return null;
            if (ItemRecycler.skin == 34537049) return null; 

            String shortname = prefab.hierachyName.Substring(prefab.hierachyName.IndexOf("/", StringComparison.Ordinal) + 1);
            if (ReplaceItem.Contains(shortname)) return null;
            
            if (player.GetParentEntity() is Tugboat)
            {
                SendChat(GetLang("ALERT_MESSAGE_INSTALL_NO_TUGBOAT", player.UserIDString), player);
                return false;
            }
            
            if (config.SettingsRecycler.InstallControllerRecycler.BlockBuildingBlock)
                if (player.GetBuildingPrivilege() == null)
                {
                    SendChat(GetLang("ALERT_MESSAGE_INSTALL_BUILDING_BLOCK", player.UserIDString), player);
                    return false;
                }

            if (!config.SettingsRecycler.InstallControllerRecycler.UseGround)
                if (target.entity == null)
                {
                    SendChat(GetLang("ALERT_MESSAGE_INSTALL_NO_GROUND", player.UserIDString), player);
                    return false;
                }

            if (config.SettingsRecycler.InstallControllerRecycler.BlockRaidBlock)
                if (IsRaidBlocked(player))
                {
                    SendChat(GetLang("ALERT_MESSAGE_INSTALL_NO_RAIDBLOCK", player.UserIDString), player);
                    return false;
                }

            if (!CheckNearbyCollider(planner, target))
            {
                SendChat(GetLang("ALERT_MESSAGE_INSTALL_NO_RECYCLER_DISTANCE", player.UserIDString), player);
                return false;
            }

            return null;
        }
        
        object OnItemRepair(BasePlayer player, Item item)
        {
            if (IsRecyclerSkinID(item.skin)) return false;
            return null;
        }

                
        
        private void RepairRecycler(BasePlayer player, Recycler recycler)
        {
            if (!IsValidRecycler(recycler)) return;
            if (RecyclerRepository[recycler.OwnerID][recycler.net.ID.Value].Health >= config.SettingsRecycler.HealthCount) return;
            
            if (config.SettingsRecycler.RepairControllerRecycler.UseDamageController)
            {
                if (IsLastDamage(recycler))
                {
                    SendChat(GetLang("ALERT_MESSAGE_REPAIR_DAMAGED_RECYCLER", player.UserIDString), player);
                    return;
                }
            }
		   		 		  						  	   		  	   		  						  						  	 	 
            Tuple<String, List<Configuration.ItemPreset>> HasRepairTuple = HasRepair(player, RecyclerRepository[recycler.OwnerID][recycler.net.ID.Value].Health, config.SettingsRecycler.HealthCount);
            String repairItemRequiered = HasRepairTuple.Item1;
            if (!String.IsNullOrWhiteSpace(repairItemRequiered))
            {
                SendChat(GetLang("ALERT_MESSAGE_REPAIR_NO_ITEMS", player.UserIDString, repairItemRequiered), player);
                return;
            }

            List<Int32> ItemList = new List<Int32>();
            Int32 Index = 0;

            foreach (Configuration.ItemPreset ItemTake in HasRepairTuple.Item2)
            {
                ItemList.Add(Convert.ToInt32(ItemTake.Amount));
                foreach (Item ItemPlayer in Enumerable.Concat(player.inventory.containerMain?.itemList ?? Enumerable.Empty<Item>(), Enumerable.Concat(player.inventory.containerBelt?.itemList ?? Enumerable.Empty<Item>(), player.inventory.containerWear?.itemList ?? Enumerable.Empty<Item>())).Where(x => x.skin == ItemTake.SkinID && x.info.shortname == ItemTake.Shortname))
                {
                    if (ItemList[Index] <= 0) continue;
                    ItemList[Index] -= ItemPlayer.amount;
                    ItemPlayer.UseItem(ItemList[Index] > 0 ? ItemList[Index] : ItemTake.Amount);
                }
                Index++;
            }

            RecyclerRepository[recycler.OwnerID][recycler.net.ID.Value].Health += config.SettingsRecycler.RepairControllerRecycler.HealthAmount;

            if (RecyclerRepository[recycler.OwnerID][recycler.net.ID.Value].Health >= config.SettingsRecycler.HealthCount)
                RecyclerRepository[recycler.OwnerID][recycler.net.ID.Value].Health = config.SettingsRecycler.HealthCount;
		   		 		  						  	   		  	   		  						  						  	 	 
            RunEffect(player, "assets/bundled/prefabs/fx/build/repair_full_metal.prefab");
            DrawUI_Progress_Health(player, RecyclerRepository[recycler.OwnerID][recycler.net.ID.Value].Health);
        }
        
        private void DrawUI_Button_Created(BasePlayer player)
        {
            if (_interface == null) return;
            
            String Interface = InterfaceBuilder.GetInterface("UI_Crafting_Button");
            if (Interface == null) return;

            String HasCrafting = this.HasCrafting(player);
            Interface = Interface.Replace("%UI_CRAFT_PANEL_BUTTON_CRAFTING%", String.IsNullOrWhiteSpace(HasCrafting) ? GetLang("UI_CRAFT_PANEL_BUTTON_CRAFTING", player.UserIDString) : HasCrafting);
            Interface = Interface.Replace("%UI_CRAFT_PANEL_BUTTON_COMMAND_CRAFTING%", String.IsNullOrWhiteSpace(HasCrafting) ? "craft.recycler" : "");
            
            CuiHelper.AddUi(player, Interface);
        }
       
        private void DrawUI_Progress_Health(BasePlayer player, Single Health)
        {
            if (_interface == null) return;

            Int32 HealthMaximum = config.SettingsRecycler.HealthCount;
            
            String Interface = InterfaceBuilder.GetInterface("UI_Health_Panel_ProgressBar");
            if (Interface == null) return;

            Interface = Interface.Replace("%HEALTH%", $"<b>{(Int32)Math.Round(Health)} / {HealthMaximum}</b>");
            Interface = Interface.Replace("%X%", $"{Health / HealthMaximum}");
		   		 		  						  	   		  	   		  						  						  	 	 
            CuiHelper.AddUi(player, Interface);
        }

        void RunEffect(BasePlayer player, String Path)
        {
            Effect effect = new Effect(Path, player, 0, new Vector3(), new Vector3());
            EffectNetwork.Send(effect, player.Connection);
        }
        
                
        
        private static InterfaceBuilder _interface;
        
        private static Boolean UsedSpeedRecycled()
        {
            if (config.DefaultSettingRecycle.SpeedRecycledController.UseSpeed)
                return true;
            
            Int32 usedBlackListCount = config.PresetsRecycle.Values.Count(t => t.SpeedRecycledController.UseSpeed);
            return usedBlackListCount != 0;
        }
        private static Boolean UsedBlackList()
        {
            if (config.DefaultSettingRecycle.BlackListsController.UseBlackList)
                return true;
            
            Int32 usedBlackListCount = config.PresetsRecycle.Values.Count(t => t.BlackListsController.UseBlackList);
            return usedBlackListCount != 0;
        }

        void WriteData()
        {
            Oxide.Core.Interface.Oxide.DataFileSystem.WriteObject("IQSystem/IQRecycler/RecyclerRepository", RecyclerRepository);
            Oxide.Core.Interface.Oxide.DataFileSystem.WriteObject("IQSystem/IQRecycler/LimitUsers", LimitUsers);
        } 

        
        
		        private static Configuration config = new Configuration();
		   		 		  						  	   		  	   		  						  						  	 	 
        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hitInfo)
        {
            if (hitInfo == null || entity == null || hitInfo.WeaponPrefab == null)
                return null;

            BasePlayer attacker = hitInfo.InitiatorPlayer;
            if (attacker == null) return null;
            if (!attacker.userID.IsSteamId()) return null;

            Recycler recycler = entity as Recycler;
            if (recycler == null || !IsValidRecycler(recycler)) return null;

            String GetShortname = PrefabIdToShortname.ContainsKey(hitInfo.WeaponPrefab.prefabID)
                ? PrefabIdToShortname[hitInfo.WeaponPrefab.prefabID]
                : ShortPrefabToShortname.ContainsKey(hitInfo.WeaponPrefab.ShortPrefabName)
                    ? ShortPrefabToShortname[hitInfo.WeaponPrefab.ShortPrefabName]
                    : null;

            if (GetShortname == null) return null;

            DamageRecycler(ItemManager.FindItemDefinition(GetShortname), recycler);
            return null;
        }
        /// <summary>
        /// Изменения :
        /// - Добавлен запрет установки переработчика на буксир
        /// - Добавлен дополнительный пункт для проверки перерабатываемого предмета с заменой на новые ресурсы (при необходимости)
        /// - Добавлены права - iqrecycler.ignorepayment , с помощью которых игроку не придется платить за переработчик монетами с помощью плагинов экономики
        /// </summary>
        
        private const Boolean LanguageEn = false;

        private void OnServerInitialized()
        {
            AddComponentAllRecyclers();

            foreach (String presetsRecycleKey in config.PresetsRecycle.Keys)
                permission.RegisterPermission(presetsRecycleKey, this);
            
            permission.RegisterPermission(PermissionCrafting, this);
            permission.RegisterPermission(PermissionIgnorePayment, this);

            if (!UsedSpeedRecycled())
                Unsubscribe("OnRecyclerToggle");
            
            if (!UsedItemRecycled())
                Unsubscribe("OnItemRecycleAmount");

            if (!UsedBlackList())
            {
                Unsubscribe("OnLootEntityEnd");
                Unsubscribe("CanAcceptItem");
                Unsubscribe("OnItemSplit");
                Unsubscribe("OnItemAction");
            }

            if (config.CraftingRecyclers.UseCraftRecycler)
            {
                _imageUI = new ImageUI();
                _imageUI.DownloadImage();
                
                cmd.AddChatCommand("rec", this, nameof(DrawUI_StaticCrafting));
                cmd.AddChatCommand("recycler", this, nameof(DrawUI_StaticCrafting));
                cmd.AddConsoleCommand("rec", this, nameof(DrawUI_StaticCrafting));
                cmd.AddConsoleCommand("recycler", this, nameof(DrawUI_StaticCrafting));
            }
            else _interface = new InterfaceBuilder(false);

            if(!config.SettingsRecycler.RepairControllerRecycler.UseReapir)
                Unsubscribe("OnHammerHit");
                
            Unsubscribe("OnEntityTakeDamage");

            if (config.SettingsRecycler.DamageControllerRecycler.UseDamageRecycler)
            {
                foreach (ItemDefinition itemDef in ItemManager.GetItemDefinitions())
                {
                    Item newItem = ItemManager.CreateByName(itemDef.shortname, 1, 0);

                    BaseEntity heldEntity = newItem.GetHeldEntity();

                    if (heldEntity != null)
                        PrefabIdToShortname[heldEntity.prefabID] = itemDef.shortname;

                    ItemModDeployable deployableMod = itemDef.GetComponent<ItemModDeployable>();
                    if (deployableMod != null && deployableMod.entityPrefab != null)
                    {
                        String deployablePrefab = deployableMod.entityPrefab.resourcePath;
                        if (String.IsNullOrEmpty(deployablePrefab))
                            continue;

                        BaseEntity prefabEntity = GameManager.server.FindPrefab(deployablePrefab)?.GetComponent<BaseEntity>();
                        if (prefabEntity != null)
                        {
                            String shortPrefabName = prefabEntity.ShortPrefabName;
                            if (!String.IsNullOrEmpty(shortPrefabName) &&
                                !ShortPrefabToShortname.ContainsKey(shortPrefabName))
                                ShortPrefabToShortname.Add(shortPrefabName, itemDef.shortname);
                        }
                    }
                    
                    newItem.Remove();
                }
                
                Subscribe("OnEntityTakeDamage");
            }
        }

        
        
        private void Init()
        {
            ReadData();
            _ = this;
        }

        private List<UInt64> GetAllIDsRecycler()
        {
            List<UInt64> AllIDs = new List<UInt64>();

            foreach (Dictionary<UInt64,RecyclerInfo> recyclerRepositoryValue in RecyclerRepository.Values)
            {
                foreach (UInt64 IDs in recyclerRepositoryValue.Keys)
                {
                    if (!AllIDs.Contains(IDs))
                        AllIDs.Add(IDs);
                }
            }

            return AllIDs;
        }
        
        
        private class ImageUI
        {
            private const String _path = "IQSystem/IQRecycler/Images/";
            private const String _printPath = "data/" + _path;
            private readonly Dictionary<string, ImageData> _images;

            private enum ImageStatus
            {
                NotLoaded,
                Loaded,
                Failed
            }
            
            public ImageUI()
            {
		   		 		  						  	   		  	   		  						  						  	 	 
                _images = new Dictionary<string, ImageData>
                {
                    { "STATIC_MENU", new ImageData() },
                    { "BLOCK_ITEM_REQUIRED", new ImageData() },
                    { "ITEM_LOGO", new ImageData() },
                    { "ITEM_REQUIRED_EMPTY", new ImageData() },
                };
            }

            private class ImageData
            {
                public ImageStatus Status = ImageStatus.NotLoaded;
                public string Id { get; set; }
            }

            public string GetImage(string name)
            {
                ImageData image;
                if (_images.TryGetValue(name, out image) && image.Status == ImageStatus.Loaded)
                    return image.Id;
                return null;
            }

            public void DownloadImage()
            {
                KeyValuePair<string, ImageData>? image = null;
                foreach (KeyValuePair<string, ImageData> img in _images)
                {
                    if (img.Value.Status == ImageStatus.NotLoaded)
                    {
                        image = img;
                        break;
                    }
                }

                if (image != null)
                {
                    ServerMgr.Instance.StartCoroutine(ProcessDownloadImage(image.Value));
                }
                else
                {
                    List<string> failedImages = new List<string>();

                    foreach (KeyValuePair<string, ImageData> img in _images)
                    {
                        if (img.Value.Status == ImageStatus.Failed)
                        {
                            failedImages.Add(img.Key);
                        }
                    }

                    if (failedImages.Count > 0)
                    {
                        string images = string.Join(", ", failedImages);
                        _.PrintError(LanguageEn
                            ? $"Failed to load the following images: {images}. Perhaps you did not upload them to the '{_printPath}' folder."
                            : $"Не удалось загрузить следующие изображения: {images}. Возможно, вы не загрузили их в папку '{_printPath}'.");
                        Interface.Oxide.UnloadPlugin(_.Name);
                    }
                    else
                    {
                        _.Puts(LanguageEn
                            ? $"{_images.Count} images downloaded successfully!"
                            : $"{_images.Count} изображений успешно загружено!");
                        
                        _interface = new InterfaceBuilder(true);
                    }
                }
            }
            
            public void UnloadImages()
            {
                foreach (KeyValuePair<string, ImageData> item in _images)
                    if(item.Value.Status == ImageStatus.Loaded)
                        if (item.Value?.Id != null)
                            FileStorage.server.Remove(uint.Parse(item.Value.Id), FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);

                _images?.Clear();
            }

            private IEnumerator ProcessDownloadImage(KeyValuePair<string, ImageData> image)
            {
                string url = "file://" + Interface.Oxide.DataDirectory + Path.DirectorySeparatorChar + _path + image.Key + ".png";

                using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(url))
                {
                    yield return www.SendWebRequest();

                    if (www.isNetworkError || www.isHttpError)
                    {
                        image.Value.Status = ImageStatus.Failed;
                    }
                    else
                    {
                        Texture2D tex = DownloadHandlerTexture.GetContent(www);
                        image.Value.Id = FileStorage.server.Store(tex.EncodeToPNG(), FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID).ToString();
                        image.Value.Status = ImageStatus.Loaded;
                        UnityEngine.Object.DestroyImmediate(tex);
                    }
		   		 		  						  	   		  	   		  						  						  	 	 
                    DownloadImage();
                }
            }
        }
        
        
        private const String RecyclerPrefab = "assets/bundled/prefabs/static/recycler_static.prefab";
        private void RemoveComponentAllRecyclers()
        {
            Int32 RecyclerUnloadAmount = 0;
            foreach (Recycler recycler in BaseNetworkable.serverEntities.ToList().Where(x => x != null && x is Recycler && GetAllIDsRecycler().Contains(x.net.ID.Value)))
            {
                recycler.gameObject.GetComponent<RecyclerController>()?.Kill();
                RecyclerUnloadAmount++;
            }

            Puts(LanguageEn ? $"{RecyclerUnloadAmount} recyclers have been unloaded" : $"{RecyclerUnloadAmount} переработчиков было выгружено");
        }
        object OnHammerHit(BasePlayer player, HitInfo info)
        {
            if (player == null || info == null || info?.HitEntity == null)
                return null;
            
            if (info.HitEntity is Recycler)
            {
                Recycler recycler = info.HitEntity as Recycler;
                if (recycler != null && recycler.OwnerID != 0 && recycler.OwnerID.IsSteamId())
                    RepairRecycler(player, recycler);
            }
            return null;
        }
        
        private Single GetResultCondition(Recycler recycler)
        {
            Single HealthToCondition = (_.GetHealthRecycler(recycler) * 1.0f) / config.SettingsRecycler.HealthCount;
            Single ResultCondition = HealthToCondition - config.SettingsRecycler.PickUpControllerRecycler.ConditionSettings.GetPercentCondition() / 100.0f;
            return ResultCondition;
        } 
        private Item API_GetItemRecyclerAfterRemove(Recycler recycler, BasePlayer player) => config.ItemSetting.GetRecyclerItem(player, 1, GetResultCondition(recycler));

        void OnEntityBuilt(Planner plan, GameObject go)
        {
            BasePlayer player = plan.GetOwnerPlayer();
            if (player == null) return;
            
            Item ItemRecycler = player.GetActiveItem();
            if (ItemRecycler == null) return;
            
            BaseEntity entity = go.ToBaseEntity();
            if (entity == null) return;
            
            if (!IsRecyclerSkinID(ItemRecycler.skin)) return;

            SpawnRecycler(player.userID, entity, ItemRecycler);
        }
        private bool CheckNearbyCollider(Planner planner, Construction.Target target)
        {
            Vector3 position;
            Vector3 size;
            Quaternion rotation = Quaternion.identity;
            if (target.entity == null)
            {
                position = planner.transform.position;
                size = new Vector3(3f, 3f, 3f);
                size /= 2f;
            }
            else
            {
                position = target.position; 
                rotation = Quaternion.Euler(target.rotation);
                Renderer renderer = target.entity.GetComponentInChildren<Renderer>();
                size = renderer != null ? renderer.bounds.size : Vector3.one;
                size /= 6f;
            }

            Collider[] colliders = Physics.OverlapBox(position, size, rotation);

            foreach (Collider collider in colliders)
            {
                if (collider.GetComponent<Recycler>() != null || collider.gameObject.name == "RECYCLER_COLLIDER")
                {
                    return false;
                }
            }

            return true;
        }

        
        
        private Single GetSpeed(Recycler recycler)
        {
            Configuration.PresetRecycle.SpeedRecycled speedRecycled = GetPresetRecycle(recycler.OwnerID).SpeedRecycledController;
            return !speedRecycled.UseSpeed ? 5f : speedRecycled.RecycledSeconds;
        }
        private const String PermissionIgnorePayment = "iqrecycler.ignorepayment";
        protected override void SaveConfig() => Config.WriteObject(config);
        void SetFlagItem(Item item, Boolean flagStatus)
        {
            if (item == null) return;
            if (!flagStatus && item.flags == global::Item.Flag.None) return;
            if (flagStatus && item.HasFlag(global::Item.Flag.IsLocked)) return;

            if (item.flags != global::Item.Flag.IsLocked)
                item.SetFlag(item.flags, false);

            item.SetFlag(global::Item.Flag.IsLocked, flagStatus);
        }
        
        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (container == null || item == null || container.playerOwner == null || !container.playerOwner.userID.IsSteamId())
                return;
            
            if (IsRecyclerSkinID(item.skin))
                item.name = config.ItemSetting.DisplayName.GetLanguageText(container.playerOwner);
        }
        
        private void ClearData()
        {
            RecyclerRepository.Clear();
            WriteData();
        }

        
        void OnRecyclerToggle(Recycler recycler, BasePlayer player)
        {
            if (!recycler.IsOn())
            {
                NextTick(() =>
                {
                    if (!recycler.IsOn())
                        return;
            
                    Single Speed = GetSpeed(recycler);
                    recycler.InvokeRepeating(recycler.RecycleThink, Speed, Speed);
                });
            }
        }

        
                
        
        private void CraftRecycler(BasePlayer player)
        {
            if (!String.IsNullOrWhiteSpace(HasCrafting(player))) return;
            
            List<Int32> ItemList = new List<Int32>();
            Int32 Index = 0;
		   		 		  						  	   		  	   		  						  						  	 	 
            foreach (Configuration.ItemPreset ItemTake in config.CraftingRecyclers.ItemCraftings)
            {
                ItemList.Add(ItemTake.Amount);
                foreach (Item ItemPlayer in Enumerable.Concat(player.inventory.containerMain?.itemList ?? Enumerable.Empty<Item>(), Enumerable.Concat(player.inventory.containerBelt?.itemList ?? Enumerable.Empty<Item>(), player.inventory.containerWear?.itemList ?? Enumerable.Empty<Item>())).Where(x => x.skin == ItemTake.SkinID && x.info.shortname == ItemTake.Shortname))
                {
                    if (ItemList[Index] <= 0) continue;
                    ItemList[Index] -= ItemPlayer.amount;
                    ItemPlayer.UseItem(ItemList[Index] > 0 ? ItemList[Index] : ItemTake.Amount);
                }
                Index++;
            }

            if (IsUsedEconomics && !permission.UserHasPermission(player.UserIDString, PermissionIgnorePayment))
                RemoveBalance(player.userID, config.CraftingRecyclers.PriceCrafting);
            
            DrawUI_Button_Created(player);

            Item Recycler = config.ItemSetting.GetRecyclerItem(player);
            player.GiveItem(Recycler);

            if (LimitUsers.ContainsKey(player.userID))
                LimitUsers[player.userID]++;
            else LimitUsers.Add(player.userID, 1);
        }
        private Int32 ConditionToHealth(Item ItemRecycler) => Convert.ToInt32((ItemRecycler.condition * config.SettingsRecycler.HealthCount) / 1.0f);
		   		 		  						  	   		  	   		  						  						  	 	 
                
                
                
        [ChatCommand("a.rec")]
        private void ChatCommandRec(BasePlayer player)
        {
            if (!player.IsAdmin) return;
		   		 		  						  	   		  	   		  						  						  	 	 
            Item Recycler = config.ItemSetting.GetRecyclerItem(player);
            player.GiveItem(Recycler);
        }

                
        //         //
        // private class ImageUI
        // {
        //     private const String _path = "IQSystem\\IQRecycler\\Images\\";
        //     private const String _printPath = "data\\" + _path;
        //
        //     private enum ImageStatus
        //     {
        //         NotLoaded,
        //         Loaded,
        //         Failed
        //     }
        //
        //     private class ImageData
        //     {
        //         public ImageStatus Status = ImageStatus.NotLoaded;
        //         public String Id { get; set; }
        //     }
        //
        //     private static readonly Dictionary<String, ImageData> _images = new Dictionary<String, ImageData>
        //     {
        //         { "STATIC_MENU", new ImageData() },
        //         { "BLOCK_ITEM_REQUIRED", new ImageData() },
        //         { "ITEM_LOGO", new ImageData() },
        //         { "ITEM_REQUIRED_EMPTY", new ImageData() },
        //     };
        //
        //     public static String GetImage(String name)
        //     {
        //         ImageData image;
        //         if (_images.TryGetValue(name, out image) && image.Status == ImageStatus.Loaded)
        //             return image.Id;
        //         return null;
        //     }
        //
        //     public static void DownloadImage()
        //     {
        //         KeyValuePair<String, ImageData> image = _images.FirstOrDefault(img => img.Value.Status == ImageStatus.NotLoaded);
        //         if (image.Value != null)
        //         {
        //             ServerMgr.Instance.StartCoroutine(ProcessDownloadImage(image));
        //         }
        //         else
        //         {
        //             List<String> failedImages = _images.Where(img => img.Value.Status == ImageStatus.Failed).Select(img => img.Key).ToList();
        //
        //             if (failedImages.Count > 0)
        //             {
        //                 String images = String.Join(", ", failedImages);
        //                 _.PrintError(LanguageEn
        //                     ? $"Failed to load the following images: {images}. Perhaps you did not upload them to the '{_printPath}' folder."
        //                     : $"Не удалось загрузить следующие изображения: {images}. Возможно, вы не загрузили их в папку '{_printPath}'.");
        //                 Interface.Oxide.UnloadPlugin(_.Name);
        //             }
        //             else
        //             {
        //                 _.Puts(LanguageEn
        //                     ? $"{_images.Count} images downloaded successfully!"
        //                     : $"{_images.Count} изображений успешно загружено!");
        //
        //                 _interface = new InterfaceBuilder(true);
        //             }
        //
        //         }
        //     }
        //
        //     private static IEnumerator ProcessDownloadImage(KeyValuePair<String, ImageData> image)
        //     {
        //         String url = "file:\\" + Interface.Oxide.DataDirectory + Path.DirectorySeparatorChar + _path + image.Key + ".png";
        //         
        //         using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(url))
        //         {
        //             yield return www.SendWebRequest();
        //
        //             if (www.isNetworkError || www.isHttpError)
        //             {
        //                 image.Value.Status = ImageStatus.Failed;
        //             }
        //             else
        //             {
        //                 Texture2D tex = DownloadHandlerTexture.GetContent(www);
        //                 image.Value.Id = FileStorage.server.Store(tex.EncodeToPNG(), FileStorage.Type.png,
        //                     CommunityEntity.ServerInstance.net.ID).ToString();
        //                 image.Value.Status = ImageStatus.Loaded;
        //                 UnityEngine.Object.DestroyImmediate(tex);
        //             }
        //
        //             DownloadImage();
        //         }
        //     }
        //     
        //     public static void UnloadImages()
        //     {
        //         foreach (KeyValuePair<String, ImageData> item in _images)
        //             if(item.Value.Status == ImageStatus.Loaded)
        //                 FileStorage.server.Remove(uint.Parse(item.Value.Id), FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
        //     }
        // }
        //
        // 
        
        private String canRemove(BasePlayer player, BaseEntity recycler) 
        {
            if (!(recycler is Recycler)) return null;
            if (!IsValidRecycler(recycler as Recycler))
                return GetLang("ALERT_MESSAGE_API_CAN_REMOVE", player.UserIDString);
            
            if (config.SettingsRecycler.PickUpControllerRecycler.OnlyOwnerPickUP)
            {
                if (player.userID != recycler.OwnerID)
                {
                    UInt64[] FriendList = GetFriendList(player);
                    if (FriendList == null) return GetLang("ALERT_MESSAGE_API_CAN_REMOVE_FRIENDS", player.UserIDString);
                    if (!FriendList.Contains(recycler.OwnerID)) return GetLang("ALERT_MESSAGE_API_CAN_REMOVE_FRIENDS", player.UserIDString);
                }
            }

            if (config.SettingsRecycler.PickUpControllerRecycler.NoPickUpBuildingBlock)
                if (player.IsBuildingBlocked())
                    return GetLang("ALERT_MESSAGE_API_CAN_REMOVE_BUILDING_BLOCK", player.UserIDString);
		   		 		  						  	   		  	   		  						  						  	 	 
            return null;
        }

                
        private void SpawnRecycler(UInt64 OwnerID, BaseEntity entity, Item ItemRecycler)
        {
            Recycler recycler = GameManager.server.CreateEntity(RecyclerPrefab, entity.transform.position, entity.transform.rotation) as Recycler;
            recycler.OwnerID = OwnerID;
            recycler.Spawn();
            
            recycler.gameObject.AddComponent<RecyclerController>();
            
            if (!RecyclerRepository.ContainsKey(OwnerID))
                RecyclerRepository.Add(OwnerID, new Dictionary<UInt64, RecyclerInfo>());

            if (!RecyclerRepository[OwnerID].ContainsKey(recycler.net.ID.Value))
                RecyclerRepository[OwnerID].Add(recycler.net.ID.Value, new RecyclerInfo()
                {
                    Health = ConditionToHealth(ItemRecycler),
                    LastDamageTime = config.SettingsRecycler.RepairControllerRecycler.SecondLastDamage,
                });

            
            NextTick(() => { entity.Kill(); });
        }
        
        private void DrawUI_StaticCrafting(BasePlayer player)
        {
            if (_interface == null) return;
            if (!permission.UserHasPermission(player.UserIDString, PermissionCrafting))
            {
                SendChat(GetLang("ALERT_MESSAGE_NOT_PERMS", player.UserIDString), player);
                return;
            }
            CuiHelper.DestroyUi(player, InterfaceBuilder.UI_CRAFT_RECYCLER);

            String Interface = InterfaceBuilder.GetInterface("UI_Crafting_Static");
            if (Interface == null) return;
            
            Interface = Interface.Replace("%UI_CRAFT_PANEL_TITLE%", GetLang("UI_CRAFT_PANEL_TITLE", player.UserIDString));
            Interface = Interface.Replace("%UI_CRAFT_PANEL_DESCRIPTION%", GetLang("UI_CRAFT_PANEL_DESCRIPTION_V2", player.UserIDString));
            Interface = Interface.Replace("%UI_CRAFT_PANEL_REQUIRED_ITEMS%", GetLang("UI_CRAFT_PANEL_REQUIRED_ITEMS", player.UserIDString));
            Interface = Interface.Replace("%UI_CRAFT_PANEL_NEED_MONEY%", !permission.UserHasPermission(player.UserIDString, PermissionIgnorePayment) ? GetLang("UI_CRAFT_PANEL_NEED_MONEY", player.UserIDString, config.CraftingRecyclers.PriceCrafting) : String.Empty);

            CuiHelper.AddUi(player, Interface);

            DrawUI_Button_Created(player);

            for (Int32 i = 0; i < 5; i++)
            {
                if (config.CraftingRecyclers.ItemCraftings.Count - 1 >= i)
                {
                    Configuration.ItemPreset ItemRequired = config.CraftingRecyclers.ItemCraftings[i];
                    DrawUI_ItemRequired(player, i, ItemRequired.Shortname, ItemRequired.SkinID, ItemRequired.Amount);
                }
                else DrawUI_ItemRequiredEmpty(player, i);
            }
        }   
        
        
        
        public Boolean IsRaidBlocked(BasePlayer player)
        {
            Object canTeleportResult = Interface.Call("CanTeleport", player);
            String ret = canTeleportResult as String;

            Object isRB = Interface.Call("IsRaidBlocked", player);
            Boolean isRaidBlocked = isRB as Boolean? ?? false;
            
            return ret != null || isRaidBlocked;
        }
        
        private List<Configuration.ItemPreset> CalculateRepairCost(Single health, Single maxHealth, List<Configuration.ItemPreset> repairItems)
        {
            Single maxHealthPerRepair = config.SettingsRecycler.RepairControllerRecycler.HealthAmount;
            Single healthDifference = maxHealthPerRepair / ((maxHealth - health) >= maxHealthPerRepair ? maxHealthPerRepair : (maxHealth - health));

            List<Configuration.ItemPreset> requiredItems = new List<Configuration.ItemPreset>();

            foreach (var item in repairItems)
            {
                Int32 AmountResult = Convert.ToInt32(((Single)item.Amount / healthDifference));
                Configuration.ItemPreset requiredItem = new Configuration.ItemPreset
                {
                    Shortname = item.Shortname,
                    Amount = AmountResult <= 0 ? 1 : AmountResult,
                    SkinID = item.SkinID,
                    TitleItems = item.TitleItems
                };

                requiredItems.Add(requiredItem);
            }
		   		 		  						  	   		  	   		  						  						  	 	 
            return requiredItems;
        }

                private ImageUI _imageUI;

                
        
        private Dictionary<UInt64, Dictionary<UInt64, RecyclerInfo>> RecyclerRepository = new Dictionary<UInt64, Dictionary<UInt64, RecyclerInfo>>();

        
        private void BlackListShowOrHide(BasePlayer player, UInt64 RecyclerOwnerID, Boolean ShowOrHide)
        {
            Configuration.PresetRecycle.BlackListRecycled BlackList = GetPresetRecycle(RecyclerOwnerID).BlackListsController;
            if (!BlackList.UseBlackList) return;
		   		 		  						  	   		  	   		  						  						  	 	 
            foreach (Item itemInventory in Enumerable.Concat(player.inventory.containerMain?.itemList ?? Enumerable.Empty<Item>(), Enumerable.Concat(player.inventory.containerBelt?.itemList ?? Enumerable.Empty<Item>(), player.inventory.containerWear?.itemList ?? Enumerable.Empty<Item>())))
            {
                if (!BlackList.IsBlackList(itemInventory)) continue;
                SetFlagItem(itemInventory, ShowOrHide);
            }
            
            player.SendNetworkUpdate();
        }

        private Boolean IsRemovedBalance(UInt64 userID, Int32 Amount)
        {
            if (IQEconomic != null)
                return (Boolean)IQEconomic?.Call("API_IS_REMOVED_BALANCE", userID, Amount);
            else if (Economics != null || ServerRewards)
                return GetBalance(userID) >= Amount;
            return false;
        }
        private Boolean API_IsValidRecycler(Recycler recycler) => IsValidRecycler(recycler);

        protected override void LoadDefaultConfig() => config = Configuration.GetNewConfiguration();
        
        
        private Boolean IsLimitUser(BasePlayer player)
        {
            if (!config.CraftingRecyclers.limitCrafting.UseLimits) return false;
            if (!LimitUsers.ContainsKey(player.userID)) return false;
            return LimitUsers[player.userID] >= config.CraftingRecyclers.limitCrafting.LimitAmount;
        }
        private Int32 GetBalance(UInt64 userID)
        {
            if (IQEconomic != null)
                return (Int32)IQEconomic?.Call("API_GET_BALANCE", userID);
            else if (Economics != null)
                return Convert.ToInt32((Double)Economics?.Call("Balance", userID));
            else if (ServerRewards != null)
            {
                Object Coins = ServerRewards.Call("CheckPoints", userID);
                return Coins == null ? 0 : Convert.ToInt32(Coins);
            }

            return 0;
        }

        private Boolean IsBlackList(UInt64 RecyclerOwnerID, Item item)
        {
            Configuration.PresetRecycle.BlackListRecycled BlackList = GetPresetRecycle(RecyclerOwnerID).BlackListsController;
            return BlackList.UseBlackList && BlackList.IsBlackList(item);
        }

                
                
        [PluginReference] Plugin Friends, Clans, IQChat, IQEconomic, Economics, ServerRewards, IQGradeRemove;
        public String GetLang(String LangKey, String userID = null, params Object[] args)
        {
            sb.Clear();
            if (args == null) return lang.GetMessage(LangKey, this, userID);
            sb.AppendFormat(lang.GetMessage(LangKey, this, userID), args);
            return sb.ToString();
        }
        
        private Single GetHealthRecycler(Recycler recycler)
        {
            if (!IsValidRecycler(recycler)) return 0;
            return RecyclerRepository[recycler.OwnerID][recycler.net.ID.Value].Health;
        }
        private Dictionary<UInt64, Int32> LimitUsers = new Dictionary<UInt64, Int32>();
        private class RecyclerInfo
        {
            public Single Health;
            public Double LastDamageTime;
        }

        
        
        private void DamageRecycler(ItemDefinition item ,Recycler recycler)
        {
            if (item == null) return;
            if (!IsValidRecycler(recycler)) return;

            Single Damage = GetDamageFromCategory(item);
            RecyclerRepository[recycler.OwnerID][recycler.net.ID.Value].Health -= Damage;
            RecyclerRepository[recycler.OwnerID][recycler.net.ID.Value].LastDamageTime = CurrentTime + config.SettingsRecycler.RepairControllerRecycler.SecondLastDamage; 
            if (RecyclerRepository[recycler.OwnerID][recycler.net.ID.Value].Health <= 0)
                recycler.Kill(BaseNetworkable.DestroyMode.Gib);
        }

        
        
        object OnItemRecycleAmount(Item item, Int32 Amount, Recycler recycler)
        {
            if (!IsValidRecycler(recycler)) return null;
            List<Item> OutputsItem = config.DefaultSettingRecycle.ItemRecycledController.GetOutputItem(item);
            if (OutputsItem == null || OutputsItem.Count == 0) return null;
            
            item.UseItem(Amount);
            foreach (Item outputItem in OutputsItem)
                recycler.MoveItemToOutput(outputItem);

            return 0;
        }


                
        
        private Boolean IsUsedEconomics => config.CraftingRecyclers.UseEconomics && (IQEconomic || Economics || ServerRewards);

        
                private Boolean IsRare(Int32 Rare) => UnityEngine.Random.Range(0, 100) >= (100 - Rare);
        
        private void Unload()
        {
            foreach (KeyValuePair<BasePlayer, Recycler> openedRecyclers in playersOpenedRecycler)
                OnLootEntityEnd(openedRecyclers.Key, openedRecyclers.Value);

            InterfaceBuilder.DestroyAll();
		   		 		  						  	   		  	   		  						  						  	 	 
            RemoveComponentAllRecyclers();
            
            WriteData();
            
            PrefabIdToShortname.Clear();
            ShortPrefabToShortname.Clear();
            
            if (_imageUI != null)
            {
                _imageUI.UnloadImages();
                _imageUI = null;
            }
            
            _ = null;
        }

        
        
        
        
        private void AddComponentAllRecyclers()
        {
            Int32 RecyclerInitAmount = 0;
            foreach(Recycler recycler in BaseNetworkable.serverEntities.ToList().Where(x => x != null && x is Recycler))
            {
                if (!IsValidRecycler(recycler)) continue;
                recycler.gameObject.AddComponent<RecyclerController>();

                RecyclerInitAmount++;
            }
            
            Puts(LanguageEn ? $"{RecyclerInitAmount} recyclers have been initialized" : $"{RecyclerInitAmount} переработчиков было инициализировано");
        }

                private void SendChat(String Message, BasePlayer player, ConVar.Chat.ChatChannel channel = ConVar.Chat.ChatChannel.Global)
        {
            if (IQChat)
                if (config.ReferencePluginsSettings.IQChatReference.UIAlertUse)
                    IQChat?.Call("API_ALERT_PLAYER_UI", player, Message);
                else IQChat?.Call("API_ALERT_PLAYER", player, Message, config.ReferencePluginsSettings.IQChatReference.CustomPrefix, config.ReferencePluginsSettings.IQChatReference.CustomAvatar);
            else player.SendConsoleCommand("chat.add", channel, 0, Message); 
        }
		   		 		  						  	   		  	   		  						  						  	 	 
        
        
        [ConsoleCommand("give.recycler")]
        void ConsoleGiveRecycler(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null) return;

            if (!arg.HasArgs(1)) return;
            String NameOrID = arg.Args[0];
            UInt64 UserIDPlayer = Convert.ToUInt64(NameOrID.IsSteamId() ? NameOrID : covalence.Players.FindPlayer(NameOrID)?.Id);
            BasePlayer TargetPlayer = BasePlayer.FindByID(UserIDPlayer);

            if (TargetPlayer == null)
            {
                PrintWarning(LanguageEn ? "This player does not exist" : "Такого игрока не существует");
                return;
            }
            
            Item Recycler = config.ItemSetting.GetRecyclerItem(TargetPlayer);
            if (Recycler.MoveToContainer(TargetPlayer.inventory.containerMain))
            {
               Puts(LanguageEn ? $"Player {NameOrID} has successfully obtained a recycler" : $"Игрок {NameOrID} успешно получил переработчик");
               return;
            }
            
            if (Recycler.MoveToContainer(TargetPlayer.inventory.containerBelt))
            {
                Puts(LanguageEn ? $"Player {NameOrID} has successfully obtained a recycler" : $"Игрок {NameOrID} успешно получил переработчик");
                return;
            }
            
            Puts(LanguageEn ? $"Player {NameOrID} did not receive a recycler because their inventory is full." : $"Игрок {NameOrID} не получил переработчик потому-что у него полный инвентарь");
        }
        private const String ReplaceItem = "workbench3";

        private readonly Dictionary<UInt32, String> PrefabIdToShortname = new Dictionary<UInt32, String>();
        
        private Single GetDamageFromCategory(ItemDefinition item)
        {
            if (item == null)
                return 1f;
        
            String ShortnameItem = item.shortname;
            if (config.SettingsRecycler.DamageControllerRecycler.CustomDamageList.ContainsKey(ShortnameItem))
                return config.SettingsRecycler.DamageControllerRecycler.CustomDamageList[ShortnameItem];
            
            Single Damage = 0f;
        
            switch (item.category)
            {
                case ItemCategory.Weapon:
                    Damage = Oxide.Core.Random.Range(0.20f, 0.6f);
                    break;
                case ItemCategory.Tool:
                    Damage = Oxide.Core.Random.Range(0.1f, 0.3f);
                    break;
                default:
                    Damage = Oxide.Core.Random.Range(0.1f, 0.2f);
                    break;
            }
        
            if (ShortPrefabToShortname.Values.Contains(item.shortname))
                Damage = Oxide.Core.Random.Range(23f, 43f);
            
            return Damage;
        }
        private class InterfaceBuilder
        {
            
            public static InterfaceBuilder Instance;
            public const String UI_PICKUP = "UI_PICKUP";
            public const String UI_HEALTH = "UI_HEALTH";
            public const String UI_CRAFT_RECYCLER = "UI_CRAFT_RECYCLER";
            public Dictionary<String, String> Interfaces;

            
            
            public InterfaceBuilder(Boolean IsCrafting)
            {
                Instance = this;
                Interfaces = new Dictionary<String, String>();

                Building_PickUp_Static();
                Building_PickUp_ProgressBar();

                Building_Health_Static();
                Building_Health_ProgressBar();

                if (IsCrafting)
                {
                    Building_Crafting_Static();
                    Building_Button_Crafting();
                    Building_ItemRequired_Empty();
                }
            }

            public static void AddInterface(String name, String json)
            {
                if (Instance.Interfaces.ContainsKey(name))
                {
                    _.PrintError($"Error! Tried to add existing cui elements! -> {name}");
                    return;
                }

                Instance.Interfaces.Add(name, json);
            }

            public static String GetInterface(String name)
            {
                String json = String.Empty;
                if (Instance.Interfaces.TryGetValue(name, out json) == false)
                {
                    _.PrintWarning($"Warning! UI elements not found by name! -> {name}");
                }

                return json;
            }

            public static void DestroyAll()
            {
                for (Int32 i = 0; i < BasePlayer.activePlayerList.Count; i++)
                {
                    BasePlayer player = BasePlayer.activePlayerList[i];
                    DestroyPickUp(player);
                    DestroyHealth(player);
                    
                    CuiHelper.DestroyUi(player, UI_CRAFT_RECYCLER);
                }
            }
		   		 		  						  	   		  	   		  						  						  	 	 
            public static void DestroyPickUp(BasePlayer player)
            {
                CuiHelper.DestroyUi(player, UI_PICKUP);
                CuiHelper.DestroyUi(player, "ProgressBar");
                CuiHelper.DestroyUi(player, "Title_ProgressBar");
                CuiHelper.DestroyUi(player, "Sprite_ProgressBar");
            }
            
            public static void DestroyHealth(BasePlayer player)
            {
                CuiHelper.DestroyUi(player, UI_HEALTH);
                CuiHelper.DestroyUi(player, "Title_Health"); 
                CuiHelper.DestroyUi(player, "ProgressBar_Health");
            }
		   		 		  						  	   		  	   		  						  						  	 	 
            
                        private void Building_PickUp_Static()
            {
                CuiElementContainer container = new CuiElementContainer();
                
                container.Add(new CuiPanel
                {
                    FadeOut = 0.1f,
                    CursorEnabled = false,
                    Image = { FadeIn = 0.1f, Color = "1 1 1 0.4" },
                    RectTransform ={ AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-99.52 22.46", OffsetMax = "99.52 30.11" }
                },"Hud",UI_PICKUP);
		   		 		  						  	   		  	   		  						  						  	 	 
                container.Add(new CuiElement
                {
                    FadeOut = 0.1f,
                    Name = "Sprite_ProgressBar",
                    Parent = UI_PICKUP,
                    Components = {
                        new CuiImageComponent() { FadeIn = 0.1f, Color = "0.77 0.25 0.16 1", Sprite = "assets/icons/pickup.png" },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-95.86 4.41", OffsetMax = "-82.53 17.75" }
                    }
                });

                container.Add(new CuiElement
                {
                    FadeOut = 0.1f,
                    Name = "Title_ProgressBar",
                    Parent = UI_PICKUP,
                    Components = {
                        new CuiTextComponent { FadeIn = 0.1f, Text = "%UI_PICK_UP_TITLE%", Font = "robotocondensed-bold.ttf", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-79.42 3.82", OffsetMax = "-27.95 17.75" }
                    }
                });
                
                AddInterface("UI_PickUp_Panel", container.ToJson());
            }
            
            private void Building_PickUp_ProgressBar()
            {
                CuiElementContainer container = new CuiElementContainer();
                
                container.Add(new CuiPanel
                {
                    FadeOut = 0.01f, 
                    CursorEnabled = false,
                    Image = { FadeIn = 0.01f, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "%X% 1", OffsetMin = "1 0.1", OffsetMax = "1 -1"}
                },UI_PICKUP,"ProgressBar", "ProgressBar");
                
                AddInterface("UI_PickUp_Panel_ProgressBar", container.ToJson());
            }
            
            
            private void Building_Health_Static()
            {
                CuiElementContainer container = new CuiElementContainer();

                container.Add(new CuiPanel
                {
                    FadeOut = 0.1f,
                    CursorEnabled = false,
                    Image = { FadeIn = 0.1f, Color = "0.45 0.55 0.27 0.9" },
                    RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-73.51 191.77", OffsetMax = "73.51 199.42" }
                },"Hud",UI_HEALTH);
		   		 		  						  	   		  	   		  						  						  	 	 
                AddInterface("UI_Health_Panel", container.ToJson());
            }
            
            private void Building_Health_ProgressBar()
            {
                CuiElementContainer container = new CuiElementContainer();
                
                container.Add(new CuiPanel
                {
                    FadeOut = 0.01f,
                    CursorEnabled = false,
                    Image = { FadeIn = 0.01f, Color = "1 1 1 1" },
                    RectTransform ={ AnchorMin = "0 0", AnchorMax = "%X% 1", OffsetMin = "0.1 -0.1", OffsetMax = "0.1 -0.1"}
                },UI_HEALTH,"ProgressBar_Health", "ProgressBar_Health");

                container.Add(new CuiElement
                {
                    FadeOut = 0.01f,
                    Name = "Title_Health",
                    DestroyUi = "Title_Health",
                    Parent = UI_HEALTH,
                    Components = {
                        new CuiTextComponent { FadeIn = 0.01f, Text = "%HEALTH%", Font = "robotocondensed-bold.ttf", FontSize = 11, Align = TextAnchor.UpperRight, Color = "1 1 1 1" },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "14.97 3.82", OffsetMax = "75.52 17.75" }
                    }
                });
                
                AddInterface("UI_Health_Panel_ProgressBar", container.ToJson());
            }

            
            
            private void Building_Crafting_Static()
            {
                CuiElementContainer container = new CuiElementContainer();

                container.Add(new CuiPanel
                {
                    CursorEnabled = true,
                    Image = { Color = "0 0 0 0" },
                    RectTransform ={ AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-502 -196.66", OffsetMax = "498 196.66" }
                },"Overlay",UI_CRAFT_RECYCLER);
                
                container.Add(new CuiButton
                {
                    RectTransform = { AnchorMin = "-100 -100", AnchorMax = "100 100" },
                    Button = { Close = UI_CRAFT_RECYCLER, Color = "0 0 0 0" },
                    Text = { Text = "" }
                }, UI_CRAFT_RECYCLER);
                
                container.Add(new CuiElement
                {
                    Name = UI_CRAFT_RECYCLER + "_Static",
                    Parent = UI_CRAFT_RECYCLER,
                    Components =
                    {
                        new CuiRawImageComponent { Color = "1 1 1 1", Png = _._imageUI.GetImage("STATIC_MENU")},
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0", AnchorMax = "1 1"
                        }
                    }
                });

                container.Add(new CuiButton
                {
                    Button = { Color = "0 0 0 0", Close = UI_CRAFT_RECYCLER },
                    Text =
                    {
                        Text = "", Font = "robotocondensed-regular.ttf", FontSize = 14,
                        Align = TextAnchor.MiddleCenter, Color = "0 0 0 0"
                    },
                    RectTransform =
                    {
                        AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-476.6 169.06",
                        OffsetMax = "-459.93 185.73"
                    }
                }, UI_CRAFT_RECYCLER, "CloseButton");

                container.Add(new CuiElement
                {
                    Name = "TitlePlugin",
                    Parent = UI_CRAFT_RECYCLER,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "%UI_CRAFT_PANEL_TITLE%", Font = "robotocondensed-regular.ttf", FontSize = 16,
                            Align = TextAnchor.UpperLeft, Color = "1 1 1 1"
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-457.64 166.75",
                            OffsetMax = "-255.15 186.72"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Name = "Description",
                    Parent = UI_CRAFT_RECYCLER,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "%UI_CRAFT_PANEL_DESCRIPTION%",
                            Font = "robotocondensed-regular.ttf", FontSize = 14, Align = TextAnchor.UpperLeft,
                            Color = "1 1 1 1"
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                            OffsetMin = "-190.21 54.85", OffsetMax = "475.62 150.04"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Name = "TitleRequiredItem",
                    Parent = UI_CRAFT_RECYCLER,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "%UI_CRAFT_PANEL_REQUIRED_ITEMS%", Font = "robotocondensed-regular.ttf",
                            FontSize = 16, Align = TextAnchor.UpperLeft, Color = "1 1 1 1"
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-190.21 19.96",
                            OffsetMax = "200.81 44.38"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Name = "PngItem",
                    Parent = UI_CRAFT_RECYCLER,
                    Components =
                    {
                        new CuiRawImageComponent { Color = "1 1 1 1", Png = _._imageUI.GetImage("ITEM_LOGO") },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-467.53 -109.8",
                            OffsetMax = "-214.86 141.53"
                        }
                    }
                });
                
                if (_.IsUsedEconomics)
                {
                    container.Add(new CuiElement
                    {
                        Name = "IQEconomicPrice",
                        Parent = UI_CRAFT_RECYCLER,
                        Components =
                        {
                            new CuiTextComponent
                            {
                                Text = "%UI_CRAFT_PANEL_NEED_MONEY%", Font = "robotocondensed-regular.ttf", FontSize = 16,
                                Align = TextAnchor.MiddleCenter, Color = "1 1 1 1"
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-423.28 -138.09",
                                OffsetMax = "-283.51 -115.35"
                            }
                        }
                    });
                }

                AddInterface("UI_Crafting_Static", container.ToJson());
            }

            private void Building_Button_Crafting()
            {
                CuiElementContainer container = new CuiElementContainer();

                container.Add(new CuiButton
                {
                    Button = { Color = "0 0 0 0", Command = "%UI_CRAFT_PANEL_BUTTON_COMMAND_CRAFTING%" },
                    Text =
                    {
                        Text = "%UI_CRAFT_PANEL_BUTTON_CRAFTING%", Font = "robotocondensed-regular.ttf", FontSize = 18,
                        Align = TextAnchor.MiddleCenter, Color = "1 1 1 1"
                    },
                    RectTransform =
                    {
                        AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-147.33 -173.46",
                        OffsetMax = "147.33 -135.46"
                    }
                }, UI_CRAFT_RECYCLER, "ButtonCreated", "ButtonCreated");
                
                AddInterface("UI_Crafting_Button", container.ToJson());
            }

            private void Building_ItemRequired_Empty()
            {
                CuiElementContainer container = new CuiElementContainer();
		   		 		  						  	   		  	   		  						  						  	 	 
                container.Add(new CuiElement
                {
                    Name = "ItemRequired",
                    Parent = UI_CRAFT_RECYCLER,
                    Components = {
                        new CuiRawImageComponent { Color = "1 1 1 1", Png = _._imageUI.GetImage("ITEM_REQUIRED_EMPTY") },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "%OFFSET_MIN% -115.36", OffsetMax = "%OFFSET_MAX% 19.95" }
                    }
                });

                AddInterface("UI_Crafting_Item_Required_Empty", container.ToJson());
            }
            
            
                    }
        private new void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<String, String>
            {
                ["UI_PICK_UP_TITLE"] = "<b>Pickup</b>",
                
                ["UI_CRAFT_PANEL_TITLE"] = "<b>HOME RECYCLER</b>",
                ["UI_CRAFT_PANEL_DESCRIPTION_V2"] = "To craft a <color=#73C545>recycler</color>, you need to:\n- Find all the required components and place them in your inventory\n- Go to the crafting menu and click the <color=#73C545>craft</color> button\n\nYou can also purchase this recycler on our website <color=#73C545>https://hellblood.gamestores.app/</color>",
                ["UI_CRAFT_PANEL_REQUIRED_ITEMS"] = "<b>REQUIRED ITEMS FOR CRAFTING:</b>",
                ["UI_CRAFT_PANEL_NEED_MONEY"] = "Cost : {0}$",
                ["UI_CRAFT_PANEL_BUTTON_CRAFTING"] = "<b>CRAFT RECYCLER</b>",
                ["UI_CRAFT_PANEL_BUTTON_NOTHING_RESOURCE"] = "<b>INSUFFICIENT RESOURCES</b>",
                ["UI_CRAFT_PANEL_BUTTON_NOTHING_BALANCE"] = "<b>INSUFFICIENT FUNDS</b>",

                ["ALERT_MESSAGE_INSTALL_BUILDING_BLOCK"] = "You can only install the recycler within the range of <color=#CD412B>your building zone</color>",
                ["ALERT_MESSAGE_INSTALL_NO_GROUND"] = "You cannot install a recycler on <color=#CD412B>the ground</color>",
                ["ALERT_MESSAGE_INSTALL_NO_TUGBOAT"] = "You cannot install the recycler <color=#CD412B>on a tugboat</color>",
                ["ALERT_MESSAGE_INSTALL_NO_RAIDBLOCK"] = "You cannot install a recycler <color=#CD412B>during a raid block</color>",
                ["ALERT_MESSAGE_INSTALL_NO_RECYCLER_DISTANCE"] = "You cannot place a <color=#CD412B>recycler</color> next to another recycler",

                ["ALERT_MESSAGE_USE_NO_RAIDBLOCK"] = "You cannot use a <color=#CD412B>recycler</color> during a raid block",

                ["ALERT_MESSAGE_REPAIR_DAMAGED_RECYCLER"] = "You cannot repair the recycler because it was <color=#CD412B>recently attacked</color>",
                ["ALERT_MESSAGE_REPAIR_NO_ITEMS"] = "Not enough items to repair the recycler, you need: {0}",

                ["ALERT_MESSAGE_API_CAN_REMOVE"] = "You cannot destroy this recycler",
                ["ALERT_MESSAGE_API_CAN_REMOVE_FRIENDS"] = "You cannot pick up <color=#CD412B>someone else's recycler</color>",
                ["ALERT_MESSAGE_API_CAN_REMOVE_BUILDING_BLOCK"] = "You cannot pick up the recycler <color=#CD412B>within the range of someone else's cupboard</color>",

                ["ALERT_MESSAGE_LIMIT_USER"] = "You have reached the <color=#CD412B>limit</color> to create a recycler",

                ["ALERT_MESSAGE_NOT_PERMS"] = "You have <color=#CD412B>insufficient</color> permissions to use this command.",

            }, this);

            lang.RegisterMessages(new Dictionary<String, String>
            {
                ["UI_PICK_UP_TITLE"] = "<b>Подобрать</b>",
                
                ["UI_CRAFT_PANEL_TITLE"] = "<b>ДОМАШНИЙ ПЕРЕРАБОТЧИК</b>",
                ["UI_CRAFT_PANEL_DESCRIPTION_V2"] = "Чтобы скрафтить <color=#73C545>переработчик</color>, нужно:\n- Найти все необходимые компоненты и положить в инвентарь\n- Зайти в меню крафта и нажать на кнопку <color=#73C545>скрафтить</color>\n\nВы можете приобрести данный переработчик у нас на сайте <color=#73C545>https://hellblood.gamestores.app/</color>",
                ["UI_CRAFT_PANEL_REQUIRED_ITEMS"] = "<b>ТРЕБУЕМЫЕ ПРЕДМЕТЫ ДЛЯ СОЗДАНИЯ :</b>",
                ["UI_CRAFT_PANEL_NEED_MONEY"] = "Стоимость : {0}$",
                ["UI_CRAFT_PANEL_BUTTON_CRAFTING"] = "<b>СОЗДАТЬ ПЕРЕРАБОТЧИК</b>",
                ["UI_CRAFT_PANEL_BUTTON_NOTHING_RESOURCE"] = "<b>НЕДОСТАТОЧНО РЕСУРСОВ</b>",
                ["UI_CRAFT_PANEL_BUTTON_NOTHING_BALANCE"] = "<b>НЕДОСТАТОЧНО СРЕДСТВ</b>",

                ["ALERT_MESSAGE_INSTALL_BUILDING_BLOCK"] = "Вы можете установить переработчик <color=#CD412B>только в зоне действия вашего шкафа</color>",
                ["ALERT_MESSAGE_INSTALL_NO_GROUND"] = "Вы не можете установить переработчик <color=#CD412B>на землю</color>",
                ["ALERT_MESSAGE_INSTALL_NO_TUGBOAT"] = "Вы не можете установить переработчик <color=#CD412B>на буксир</color>",
                ["ALERT_MESSAGE_INSTALL_NO_RAIDBLOCK"] = "Вы не можете установить переработчик <color=#CD412B>во время рейд-блока</color>",
                ["ALERT_MESSAGE_INSTALL_NO_RECYCLER_DISTANCE"] = "Вы не можете установить переработчик <color=#CD412B>рядом с другим переработчиком</color>",
                
                ["ALERT_MESSAGE_USE_NO_RAIDBLOCK"] = "Вы не можете использовать переработчик <color=#CD412B>во время рейд-блока</color>",
                
                ["ALERT_MESSAGE_REPAIR_DAMAGED_RECYCLER"] = "Вы не можете чинить переработчик, потому что он был <color=#CD412B>недавно атакован</color>",
                ["ALERT_MESSAGE_REPAIR_NO_ITEMS"] = "Недостаточно предметов для починки переработчика, вам требуется : {0}",
                
                ["ALERT_MESSAGE_API_CAN_REMOVE"] = "Вы не можете уничтожить этот переработчик",
                ["ALERT_MESSAGE_API_CAN_REMOVE_FRIENDS"] = "Вы не можете подобрать <color=#CD412B>чужой переработчик</color>",
                ["ALERT_MESSAGE_API_CAN_REMOVE_BUILDING_BLOCK"] = "Вы не можете подобрать переработчик <color=#CD412B>в зоне действия чужого шкафа</color>",

                ["ALERT_MESSAGE_LIMIT_USER"] = "Вы <color=#CD412B>достигли лимита</color> по созданию переработчика",
                
                ["ALERT_MESSAGE_NOT_PERMS"] = "У вас <color=#CD412B>недостаточно</color> прав для использования этой команды",
            }, this, "ru");
        }

        void ReadData()
        {
            RecyclerRepository = Oxide.Core.Interface.Oxide.DataFileSystem.ReadObject<Dictionary<UInt64, Dictionary<UInt64, RecyclerInfo>>>("IQSystem/IQRecycler/RecyclerRepository");
            LimitUsers = Oxide.Core.Interface.Oxide.DataFileSystem.ReadObject<Dictionary<UInt64, Int32>>("IQSystem/IQRecycler/LimitUsers");
        }

        
        private Configuration.PresetRecycle GetPresetRecycle(UInt64 userID)
        {
            foreach (KeyValuePair<String, Configuration.PresetRecycle> presets in config.PresetsRecycle)
            {
                if (permission.UserHasPermission(userID.ToString(), presets.Key))
                    return presets.Value;
            }

            return config.DefaultSettingRecycle;
        }
        
        
                
        private void DrawUI_StaticPickUP(BasePlayer player)
        {
            if (_interface == null) return;
            InterfaceBuilder.DestroyPickUp(player);
		   		 		  						  	   		  	   		  						  						  	 	 
            String Interface = InterfaceBuilder.GetInterface("UI_PickUp_Panel");
            if (Interface == null) return;

            Interface = Interface.Replace("%UI_PICK_UP_TITLE%", GetLang("UI_PICK_UP_TITLE", player.UserIDString));

            CuiHelper.AddUi(player, Interface);
        }
		   		 		  						  	   		  	   		  						  						  	 	 
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) LoadDefaultConfig();
                if (config.CraftingRecyclers.limitCrafting == null)
                {
                    config.CraftingRecyclers.limitCrafting = new Configuration.CraftingRecycler.LimitCrafting()
                    {
                        UseLimits = false,
                        LimitAmount = 5,
                    };
                }
            }
            catch
            {
                PrintWarning(LanguageEn ? $"Error reading #54327 configuration 'oxide/config/{Name}', creating a new configuration!!" : $"Ошибка чтения #54327 конфигурации 'oxide/config/{Name}', создаём новую конфигурацию!!");
                LoadDefaultConfig();
            }

            NextTick(SaveConfig);
        }

        ItemContainer.CanAcceptResult? CanAcceptItem(ItemContainer container, Item item, int targetPos) 
        {
            if (item == null || item.info == null || container == null) return null;
            
            if(container.entityOwner is Recycler && IsBlackList(container.entityOwner.OwnerID, item))
            {
                if(item.HasFlag(global::Item.Flag.IsLocked))
                    return ItemContainer.CanAcceptResult.CannotAccept;

                SetFlagItem(item, true);
                return null;
            }
            
            return null;
        }
        
        object OnItemAction(Item item, string action, BasePlayer player)
        {
            if (item == null || player == null || String.IsNullOrWhiteSpace(action)) return null;

            if (action.Equals("drop") && item.HasFlag(global::Item.Flag.IsLocked)) 
            {
                SetFlagItem(item, false);
                NextTick(() => { SetFlagItem(item, true);});
            }

            return null;
        }

        void OnLootEntityEnd(BasePlayer player, Recycler recycler)
        {
            if (recycler != null && IsValidRecycler(recycler))
            {
                BlackListShowOrHide(player, recycler.OwnerID, false);

                NextTick(() =>
                {
                    if (playersOpenedRecycler.ContainsKey(player))
                        playersOpenedRecycler.Remove(player);
                });
            }
        }

        private void OnNewSave(String filename) => ClearData();
        
        private void DrawUI_ItemRequired(BasePlayer player, Int32 X, String Shortname, UInt64 SkinID, Int32 Amount)
        {
            if (_interface == null) return;

            CuiElementContainer container = new CuiElementContainer();

            container.Add(new CuiElement
            {
                Name = "ItemRequired",
                Parent = InterfaceBuilder.UI_CRAFT_RECYCLER,
                Components = {
                    new CuiRawImageComponent { Color = "1 1 1 1", Png = _imageUI.GetImage("BLOCK_ITEM_REQUIRED") },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-190.8 + (X * 135)} -115.36", OffsetMax = $"{-64.13+ (X * 135)} 19.96" }
                }
            });

            container.Add(new CuiElement
            {
                Name = "PngItem",
                Parent = "ItemRequired",
                Components = {
                    new CuiImageComponent() { Color = "1 1 1 1", ItemId = ItemManager.FindItemDefinition(Shortname).itemid, SkinId = SkinID },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-52 -52", OffsetMax = "52 52" }
                }
            });

            container.Add(new CuiElement
            {
                Name = "ItemAmount",
                Parent = "PngItem",
                Components = {
                    new CuiTextComponent { Text = $"<b>X{Amount}</b>", Font = "robotocondensed-regular.ttf", FontSize = 32, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-52 -52", OffsetMax = "52 52" }
                }
            });
            
            CuiHelper.AddUi(player, container);
        }    

        private Boolean IsLastDamage(Recycler recycler, Double LimitSecond = 0)
        {
            if (!IsValidRecycler(recycler)) return false;
            
            Double LeftDamage = RecyclerRepository[recycler.OwnerID][recycler.net.ID.Value].LastDamageTime - CurrentTime;
            return LeftDamage > LimitSecond;
        }
        
        private static Boolean UsedItemRecycled()
        {
            if (config.DefaultSettingRecycle.ItemRecycledController.UseItemRecycled)
                return true;
            
            Int32 usedItemRecycledCount = config.PresetsRecycle.Values.Count(t => t.ItemRecycledController.UseItemRecycled);
            return usedItemRecycledCount != 0;
        }

        private Item OnItemSplit(Item item, int amount)
        {
            if (item == null) return null;
            if (plugins.Find("Stacks") || plugins.Find("CustomSkinsStacksFix") || plugins.Find("SkinBox")) return null;
            if (item.HasFlag(global::Item.Flag.IsLocked))
            {
                Item x = ItemManager.CreateByPartialName(item.info.shortname, amount);
                x.name = item.name;
                x.skin = item.skin;
                x.amount = amount;
                x.SetFlag(global::Item.Flag.IsLocked, true);
                item.amount -= amount;
                return x;
            }
            return null;
        }
        
        private Dictionary<BasePlayer, Recycler> playersOpenedRecycler = new Dictionary<BasePlayer, Recycler>();
                
                
        
        
        
        private void DrawUI_StaticCrafting(ConsoleSystem.Arg arg)
        {
            if (arg.Player() == null) return;
            DrawUI_StaticCrafting(arg.Player());
        }
        
        private Boolean HasRequiredItems(BasePlayer player, String Shortname, Int32 Amount, UInt64 SkinID = 0)
        {
            Int32 ItemAmount = 0;
            foreach (Item ItemRequires in Enumerable.Concat(player.inventory.containerMain?.itemList ?? Enumerable.Empty<Item>(), Enumerable.Concat(player.inventory.containerBelt?.itemList ?? Enumerable.Empty<Item>(), player.inventory.containerWear?.itemList ?? Enumerable.Empty<Item>())))
            {
                if (ItemRequires == null) continue;
                if (ItemRequires.info.shortname != Shortname) continue;
                if (ItemRequires.skin != SkinID) continue;
                ItemAmount += ItemRequires.amount;
            }
            return ItemAmount >= Amount;
        }

            }
}
