using System;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Utils;
using LevelsRanksApi;
using System.Text.Json;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace LevelsRanksModuleTimeReward
{
    public class LevelsRanksModuleTimeReward : BasePlugin
    {
        public override string ModuleName => "[LR] Module - TimeReward";
        public override string ModuleVersion => "1.0.0";
        public override string ModuleAuthor => "ABKAM";

        private ILevelsRanksApi? _api;
        private readonly PluginCapability<ILevelsRanksApi> _apiCapability = new("levels_ranks");

        private int _experiencePerInterval = 10;
        private float _updateInterval = 120.0f;

        private readonly ConcurrentDictionary<string, Timer> _playerTimers = new();

        public override void Load(bool hotReload)
        {
            _api = _apiCapability.Get();
            if (_api == null)
            {
                Logger.LogError("Failed to load Levels Ranks API.");
                return;
            }

            LoadConfig(); 

            RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnect);
            RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        }
        private void LoadConfig()
        {
            var configDirectory = Path.Combine(Application.RootDirectory, "configs/plugins/LevelsRanks");
            var filePath = Path.Combine(configDirectory, "settings_timereward.json");
            
            if (!Directory.Exists(configDirectory))
            {
                try
                {
                    Directory.CreateDirectory(configDirectory);
                }
                catch (Exception ex)
                {
                    Logger.LogError("Failed to create config directory: {Message}", ex.Message);
                    return;
                }
            }

            if (!File.Exists(filePath))
            {
                CreateDefaultConfig(filePath);  
            }
            else
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    var config = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

                    if (config != null)
                    {
                        if (config.TryGetValue("ExperiencePerInterval", out var expObj) && int.TryParse(expObj.ToString(), out var exp))
                        {
                            _experiencePerInterval = exp;
                        }

                        if (config.TryGetValue("UpdateIntervalSeconds", out var intervalObj) && float.TryParse(intervalObj.ToString(), out var interval))
                        {
                            _updateInterval = interval;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("Error loading configuration: {Message}", ex.Message);
                }
            }
        }

        private void CreateDefaultConfig(string filePath)
        {
            var defaultConfig = new
            {
                ExperiencePerInterval = 10,   
                UpdateIntervalSeconds = 120.0f   
            };

            try
            {
                var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Logger.LogError("Error creating default config: {Message}", ex.Message);
            }
        }

        private HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
        {
            if (@event.Userid != null)
            {
                var steamId64 = @event.Userid.SteamID;
                var steamId = _api!.ConvertToSteamId(steamId64);

                var playerTimer = AddTimer(_updateInterval, () => RewardPlayer(steamId64), TimerFlags.REPEAT);
                _playerTimers[steamId] = playerTimer;
            }

            return HookResult.Continue;
        }

        private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
        {
            if (@event.Userid != null)
            {
                var steamId64 = @event.Userid.SteamID;
                var steamId = _api!.ConvertToSteamId(steamId64);

                if (_playerTimers.TryRemove(steamId, out var timer))
                {
                    timer.Kill();
                }
            }

            return HookResult.Continue;
        }

        private void RewardPlayer(ulong steamId64)
        {
            var steamId = _api!.ConvertToSteamId(steamId64);
            var player = Utilities.GetPlayers().FirstOrDefault(p => p.SteamID == steamId64);

            if (player != null && _api.OnlineUsers.TryGetValue(steamId, out var user))
            {
                var message = Localizer["timereward.message"]; 
                
                var color = ReplaceColorPlaceholders(Localizer["timereward.color"]);
                
                _api.ApplyExperienceUpdateSync(user, player, _experiencePerInterval, message, color);
            }
        }
        [ConsoleCommand("css_lvl_reload", "Reloads the configuration files")]
        public void ReloadConfigsCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null)
            {
                LoadConfig();
            }
        }
        public string ReplaceColorPlaceholders(string message)
        {
            if (message.Contains('{'))
            {
                var modifiedValue = message;
                foreach (var field in typeof(ChatColors).GetFields())
                {
                    var pattern = $"{{{field.Name}}}";
                    if (message.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                        modifiedValue = modifiedValue.Replace(pattern, field.GetValue(null).ToString(),
                            StringComparison.OrdinalIgnoreCase);
                }

                return modifiedValue;
            }

            return message;
        }
    }
}
