using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using Newtonsoft.Json.Linq;
using ShopAPI;

namespace ShopParachute
{
    [MinimumApiVersion(179)]
    public class Parachute : BasePlugin
    {
        public override string ModuleName => "[SHOP] Parachute";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "v1.0.1";

        private IShopApi? SHOP_API;
        private const string CategoryName = "Parachute";
        private JObject? JsonConfig;
        private readonly Dictionary<int, PlayerParachute> playerParachutes = [];

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            SHOP_API = IShopApi.Capability.Get();
            if (SHOP_API == null) return;

            LoadConfig();
            InitializeShopItems();
            SetupListeners();
        }

        private void LoadConfig()
        {
            string configPath = Path.Combine(ModuleDirectory, "../../configs/plugins/Shop/Parachute.json");
            if (File.Exists(configPath))
            {
                JsonConfig = JObject.Parse(File.ReadAllText(configPath));
            }
        }

        private void InitializeShopItems()
        {
            if (JsonConfig == null || SHOP_API == null) return;

            SHOP_API.CreateCategory(CategoryName, "Парашют");

            foreach (var item in JsonConfig.Properties().Where(p => p.Value is JObject))
            {
                Task.Run(async () =>
                {
                    int itemId = await SHOP_API.AddItem(
                        item.Name,
                        (string)item.Value["name"]!,
                        CategoryName,
                        (int)item.Value["price"]!,
                        (int)item.Value["sellprice"]!,
                        (int)item.Value["duration"]!
                    );
                    SHOP_API.SetItemCallbacks(itemId, OnClientBuyItem, OnClientSellItem, OnClientToggleItem);
                }).Wait();
            }
        }

        private void SetupListeners()
        {
            RegisterListener<Listeners.OnTick>(OnTick);
            RegisterListener<Listeners.OnClientDisconnect>(playerSlot =>
            {
                playerParachutes.Remove(playerSlot);
            });

            RegisterEventHandler<EventPlayerDeath>((@event, info) =>
            {
                CCSPlayerController? player = @event.Userid;
                if (player != null && playerParachutes.ContainsKey((int)player.Index) && playerParachutes[(int)player.Index].IsActive)
                {
                    StopParachute(player);
                }
                return HookResult.Continue;
            });

            RegisterListener<Listeners.OnServerPrecacheResources>((manifest) =>
            {
                foreach (var item in JsonConfig!.Properties().Where(p => p.Value is JObject))
                {
                    manifest.AddResource((string)item.Value["model"]!);
                }
            });
        }

        public HookResult OnClientBuyItem(CCSPlayerController player, int itemId, string categoryName, string uniqueName, int buyPrice, int sellPrice, int duration, int count)
        {
            if (JsonConfig?[uniqueName] is JObject itemConfig)
            {
                playerParachutes[(int)player.Index] = new PlayerParachute(itemId, (string)itemConfig["model"]!);
            }
            return HookResult.Continue;
        }

        public HookResult OnClientToggleItem(CCSPlayerController player, int itemId, string uniqueName, int state)
        {
            if (state == 1)
            {
                if (JsonConfig?[uniqueName] is JObject itemConfig)
                {
                    playerParachutes[(int)player.Index] = new PlayerParachute(itemId, (string)itemConfig["model"]!);
                }
            }
            else if (state == 0)
            {
                OnClientSellItem(player, itemId, uniqueName, 0);
            }
            return HookResult.Continue;
        }

        public HookResult OnClientSellItem(CCSPlayerController player, int itemId, string uniqueName, int sellPrice)
        {
            playerParachutes.Remove((int)player.Index);
            return HookResult.Continue;
        }

        private void OnTick()
        {
            var players = Utilities.GetPlayers();

            foreach (var player in players)
            {
                if (player != null
                && player.IsValid
                && !player.IsBot
                && player.PawnIsAlive
                && playerParachutes.ContainsKey((int)player.Index))
                {
                    var buttons = player.Buttons;
                    var pawn = player.PlayerPawn.Value!;
                    if ((buttons & PlayerButtons.Use) != 0 && !pawn.OnGroundLastTick && (!GetConfigValue<bool>("DisableWhenCarryingHostage") || pawn.HostageServices!.CarriedHostageProp.Value == null))
                    {
                        StartParachute(player);
                    }
                    else if (playerParachutes[(int)player.Index].IsActive)
                    {
                        StopParachute(player);
                    }
                }
            }
        }

        private void StartParachute(CCSPlayerController player)
        {
            var parachute = playerParachutes[(int)player.Index];
            if (!parachute.IsActive)
            {
                parachute.IsActive = true;
                player.GravityScale = 0.1f;

                parachute.ParachuteModel = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic_override")!;

                if (parachute.ParachuteModel != null && parachute.ParachuteModel.IsValid)
                {
                    parachute.ParachuteModel.MoveType = MoveType_t.MOVETYPE_NOCLIP;
                    parachute.ParachuteModel.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_NONE;
                    parachute.ParachuteModel.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_NONE;
                    parachute.ParachuteModel?.DispatchSpawn();
                    parachute.ParachuteModel?.SetModel(parachute.Model);
                }
            }

            var fallspeed = GetConfigValue<float>("FallSpeed") * (-1.0f);
            var isFallSpeed = false;
            var velocity = player.PlayerPawn.Value?.AbsVelocity;
            if (velocity?.Z >= fallspeed)
            {
                isFallSpeed = true;
            }

            if (velocity?.Z < 0.0f)
            {
                if (isFallSpeed && GetConfigValue<bool>("Linear") || GetConfigValue<float>("DecreaseVec") == 0.0)
                {
                    velocity.Z = fallspeed;
                }
                else
                {
                    velocity.Z = velocity.Z + GetConfigValue<float>("DecreaseVec");
                }

                var position = player.PlayerPawn.Value?.AbsOrigin!;
                var angle = player.PlayerPawn.Value?.AbsRotation!;

                if (parachute.ParaTicks > GetConfigValue<int>("TeleportTicks"))
                {
                    player.Teleport(position, angle, velocity);
                    parachute.ParaTicks = 0;
                }

                if (parachute.ParachuteModel != null && parachute.ParachuteModel.IsValid)
                {
                    parachute.ParachuteModel?.Teleport(position, angle, velocity);
                }

                ++parachute.ParaTicks;
            }
        }

        private void StopParachute(CCSPlayerController player)
        {
            player.GravityScale = 1.0f;
            if (playerParachutes.ContainsKey((int)player.Index))
            {
                var parachute = playerParachutes[(int)player.Index];
                parachute.ParaTicks = 0;
                parachute.IsActive = false;
                if (parachute.ParachuteModel != null && parachute.ParachuteModel.IsValid)
                {
                    parachute.ParachuteModel.Remove();
                    parachute.ParachuteModel = null!;
                }
            }
        }

        private T GetConfigValue<T>(string key)
        {
            if (JsonConfig != null && JsonConfig[key] != null)
            {
                return JsonConfig[key]!.ToObject<T>()!;
            }
            return default!;
        }

        public class PlayerParachute(int itemId, string model)
        {
            public int ItemID { get; set; } = itemId;
            public bool IsActive { get; set; } = false;
            public int ParaTicks { get; set; } = 0;
            public CDynamicProp ParachuteModel { get; set; } = null!;
            public string Model { get; set; } = model;
        }
    }
}