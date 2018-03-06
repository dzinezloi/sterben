using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
//using DSharpPlus.CommandsNext;
//using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using LiteDB;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TokumeBot
{
    public struct ConfigJson
    {
        [JsonProperty("token")]
        public string Token { get; private set; }

        [JsonProperty("serverId")]
        public List<ulong> ServerId { get; private set; }

        [JsonProperty("gamesDictionary")]
        public Dictionary<string, List<string>> GamesDictionary { get; private set; }

        [JsonProperty("targetAnonChannel")]
        public ulong TargetAnonChannel { get; private set; }

        [JsonProperty("embedColor")]
        public string EmbedColor { get; private set; }

        [JsonProperty("ownersId")]
        public List<ulong> OwnersId { get; private set; }
    }

    class UserModel
    {
        public ulong Id { get; set; }
        public bool rrIsEnabled { get; set; }
    }

    class Program
    {
        private static ConfigJson cfgJson;
        private DiscordClient client { get; set; }
        private CommandsNextModule commands { get; set; }
        private static Dictionary<ulong, List<DiscordRole>> targetRoles = new Dictionary<ulong, List<DiscordRole>>();
        private List<DiscordDmChannel> ownersChannels { get; set; }

        static void Main(string[] args)
        {
            var program = new Program();
            program.Run().GetAwaiter().GetResult();
        }

        private async Task Run()
        {
            try
            {
                var json = "";
                using (var fs = File.OpenRead("config.json"))
                using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                    json = await sr.ReadToEndAsync();
                cfgJson = JsonConvert.DeserializeObject<ConfigJson>(json);

                client = new DiscordClient(new DiscordConfiguration()
                {
                    AutoReconnect = true,
                    Token = cfgJson.Token,
                    TokenType = TokenType.Bot,
                    LogLevel = LogLevel.Debug,
                    UseInternalLogHandler = true
                });
                if (cfgJson.GamesDictionary == null || cfgJson.GamesDictionary.Count == 0)
                    throw new Exception("Список gamesDictionary не найден");
                if (string.IsNullOrEmpty(cfgJson.Token))
                    throw new Exception("Токен не найден");
                if (cfgJson.ServerId == null || cfgJson.ServerId.Count == 0)
                    throw new Exception("Список serverId не найден");
                if (cfgJson.TargetAnonChannel == 0)
                    throw new Exception("targetAnonChannel не найден");
                client.DebugLogger.LogMessage(LogLevel.Info, "002", "Loading...", DateTime.Now);
                client.Ready += Client_Ready;
                client.ClientErrored += Client_ClientError;
                client.PresenceUpdated += Client_PresenceUpdated;
                client.GuildAvailable += Client_GuildAvailable;
                client.MessageCreated += Client_MessageCreated;
                this.commands = this.client.UseCommandsNext(new CommandsNextConfiguration
                {
                    StringPrefix = "!",
                    EnableDms = true,
                    EnableMentionPrefix = false,
                    EnableDefaultHelp = false,
                    CaseSensitive = false
                });
                commands.RegisterCommands<Commands>();
                commands.CommandExecuted += Commands_CommandExecuted;
                await client.ConnectAsync();
            }
            catch (Exception ex)
            {
                client.DebugLogger.LogMessage(LogLevel.Error, "002", $"Exception occured: {ex.GetType()}: {ex.Message}", DateTime.Now);
            }
            await Task.Delay(-1);
        }

        private Task Commands_CommandExecuted(CommandExecutionEventArgs e)
        {
            client.DebugLogger.LogMessage(LogLevel.Info, "002", $"Command {e.Command.QualifiedName} has been executed by {e.Context.User.Username}", DateTime.Now);
            return Task.CompletedTask;
        }

        private async Task Client_MessageCreated(MessageCreateEventArgs e)
        {
            if (!e.Channel.IsPrivate || e.Author.Id == client.CurrentUser.Id)
                return;
            var targetChannel = await client.GetChannelAsync(cfgJson.TargetAnonChannel);
            if (targetChannel == null)
            {
                client.DebugLogger.LogMessage(LogLevel.Error, "002", $"Exception occured: не удалось получить targetChannel", DateTime.Now);
                return;
            }
            await e.Channel.TriggerTypingAsync();
            var embed = new DiscordEmbedBuilder
            {
                Author = new DiscordEmbedBuilder.EmbedAuthor
                {
                    Name = "Анонимное сообщение",
                    IconUrl = "https://i.imgur.com/P3OvJAq.png"
                },
                Description = e.Message.Content,
                Footer = new DiscordEmbedBuilder.EmbedFooter
                {
                    Text = "Tavern «N.E.E.T»",
                    IconUrl = "https://i.imgur.com/hKoyHp7.png"
                },

                Color = new DiscordColor(cfgJson.EmbedColor),
                Timestamp = DateTime.Now
            };
            await e.Channel.SendMessageAsync("Ваше анонимное сообщение успешно отправлено, отправитель будет скрыт.");
            await targetChannel.SendMessageAsync(embed: embed.Build());
            for (int i = 0; i < ownersChannels.Count; i++)
            {
                await ownersChannels[i].SendMessageAsync($"Отправитель: {e.Author.Username}\nСообщение: {e.Message.Content}");
            }
        }

        private async Task Client_GuildAvailable(GuildCreateEventArgs e)
        {
            try
            {
                if (cfgJson.ServerId.Contains(e.Guild.Id))
                {
                    var serverRoles = e.Guild.Roles;
                    targetRoles.Add(e.Guild.Id, new List<DiscordRole>());
                    for (int i = 0; i < serverRoles.Count; i++)
                    {
                        if (cfgJson.GamesDictionary.Keys.FirstOrDefault(x => x == serverRoles[i].Name) != null)
                            targetRoles[e.Guild.Id].Add(serverRoles[i]);
                    }
                    var users = e.Guild.Members;
                    using (var db = new LiteDatabase($"filename={cfgJson.ServerId.FirstOrDefault(x => x == e.Guild.Id)}.db; journal=false;"))
                    {
                        var dbUsers = db.GetCollection<UserModel>("Users");
                        for (int i = 0; i < users.Count; i++)
                        {
                            var dbUser = dbUsers.FindOne(x => x.Id == users[i].Id);
                            if (dbUser == null)
                            {
                                var userModel = new UserModel
                                {
                                    Id = users[i].Id,
                                    rrIsEnabled = true
                                };
                                dbUsers.Insert(userModel);
                                dbUser = dbUsers.FindOne(x => x.Id == users[i].Id);
                            }
                            var userGame = users[i].Presence?.Game?.Name?.ToLower();
                            if (!users[i].IsBot && !string.IsNullOrEmpty(userGame) && dbUser.rrIsEnabled)
                            {
                                var userRoles = users[i].Roles.ToList();
                                foreach (var game in cfgJson.GamesDictionary)
                                {
                                    if (userRoles.Exists(x => x.Name == game.Key))
                                        continue;
                                    if (game.Value.Exists(x => userGame.Contains(x)))
                                    {
                                        var nonGrantedRole = targetRoles[e.Guild.Id].Find(x => x.Name == game.Key);
                                        if (nonGrantedRole != null)
                                        {
                                            await users[i].GrantRoleAsync(nonGrantedRole);
                                            e.Client.DebugLogger.LogMessage(LogLevel.Info, "002", $"Пользователь {users[i].Username} получил роль {nonGrantedRole.Name}", DateTime.Now);
                                        }
                                        break;
                                    }
                                }
                            }
                        }                            
                    }
                }
            }
            catch (Exception ex)
            {
                e.Client.DebugLogger.LogMessage(LogLevel.Error, "002", $"Exception occured: {ex.GetType()}: {ex.Message}", DateTime.Now);
            }
            return;
        }

        private async Task Client_PresenceUpdated(PresenceUpdateEventArgs e)
        {
            try
            {
                if (!cfgJson.ServerId.Contains(e.Guild.Id) || e.Game == null)
                    return;
                using (var db = new LiteDatabase($"filename={cfgJson.ServerId.FirstOrDefault(x => x == e.Guild.Id)}.db; journal=false;"))
                {
                    var dbUsers = db.GetCollection<UserModel>("Users");
                    var dbUser = dbUsers.FindOne(x => x.Id == e.Member.Id);
                    if (dbUser == null)
                    {
                        var userModel = new UserModel
                        {
                            Id = e.Member.Id,
                            rrIsEnabled = true
                        };
                        dbUsers.Insert(userModel);
                        dbUser = dbUsers.FindOne(x => x.Id == e.Member.Id);
                    }
                    if (!dbUser.rrIsEnabled)
                        return;
                }
                var userRoles = e.Roles.ToList();
                var userGame = e.Game.Name.ToLower();
                foreach (var game in cfgJson.GamesDictionary)
                {
                    if (userRoles.Exists(x => x.Name == game.Key))
                        continue;
                    if (game.Value.Exists(x => userGame.Contains(x)))
                    {
                        var nonGrantedRole = targetRoles[e.Guild.Id].Find(x => x.Name == game.Key);
                        if (nonGrantedRole != null)
                        {
                            await e.Member.GrantRoleAsync(nonGrantedRole);
                            e.Client.DebugLogger.LogMessage(LogLevel.Info, "002", $"Пользователь {e.Member.Username} получил роль {nonGrantedRole.Name}", DateTime.Now);
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                e.Client.DebugLogger.LogMessage(LogLevel.Error, "002", $"Exception occured: {ex.GetType()}: {ex.Message}", DateTime.Now);
            }
            return;
        }

        private Task Client_ClientError(ClientErrorEventArgs e)
        {
            e.Client.DebugLogger.LogMessage(LogLevel.Error, "002", $"Exception occured: {e.Exception.GetType()}: {e.Exception.Message}", DateTime.Now);
            return Task.CompletedTask;
        }

        private async Task Client_Ready(ReadyEventArgs e)
        {
            e.Client.DebugLogger.LogMessage(LogLevel.Info, "002", "Client is ready to process events.", DateTime.Now);
            await Task.Delay(1000);
            var game = new DiscordGame
            {
                Name = "2B W ダーリン",
                Url = "https://www.twitch.tv/devimind",
                StreamType = GameStreamType.Twitch
            };
            await client.UpdateStatusAsync(game: game);
            return;
        }

        internal class Commands
        {
            [Command("removerole"), Aliases("rr"), Description("removes current game roles")]
            public async Task Remove(CommandContext ctx)
            {
                try
                {
                    DiscordEmbedBuilder embed;
                    await ctx.TriggerTypingAsync();
                    var userRoles = (ctx.User as DiscordMember).Roles.ToList();
                    var arg = ctx.RawArgumentString?.Trim().ToLower();
                    var targetRole = userRoles.Find(x => x.Name.ToLower() == arg);
                    string embedMessage = "";
                    if (arg == "all")
                    {
                        for (int i = 0; i < userRoles.Count; i++)
                        {
                            if (targetRoles[ctx.Guild.Id].Exists(x => x.Name == userRoles[i].Name))
                                await (ctx.User as DiscordMember).RevokeRoleAsync(userRoles[i]);
                        }
                        embedMessage = $"{ctx.Member.Mention} снял с себя все игровые роли.";
                    }
                    else if (arg == "off" || arg == "on")
                    {
                        using (var db = new LiteDatabase($"filename={cfgJson.ServerId.FirstOrDefault(x => x == ctx.Guild.Id)}.db; journal=false;"))
                        {
                            var dbUsers = db.GetCollection<UserModel>("Users");
                            var dbUser = dbUsers.FindOne(x => x.Id == ctx.Member.Id);
                            if (dbUser == null)
                            {
                                var userModel = new UserModel
                                {
                                    Id = ctx.Member.Id,
                                    rrIsEnabled = false
                                };
                                dbUsers.Insert(userModel);
                                dbUser = dbUsers.FindOne(x => x.Id == ctx.Member.Id);
                            }
                            dbUser.rrIsEnabled = arg == "off" ? false : true;
                            embedMessage = arg == "off" ? $"{ctx.Member.Mention} отключил автополучение игровых ролей." : $"{ctx.Member.Mention} включил автополучение игровых ролей.";
                            dbUsers.Update(dbUser);
                        }
                    }
                    else if (string.IsNullOrEmpty(arg))
                    {
                        embedMessage = "**!rr** и **!removerole** для снятия с cебя указанной роли.\n" +
                            "Для снятия сразу всех игровых ролей используйте роль ALL.\n" +
                            "Например **!rr GTA** или **!rr ALL**\n" +
                            "**!rr off** для отключение автополучения игровых ролей.";
                    }
                    else if (!targetRoles[ctx.Guild.Id].Exists(x => x.Name.ToLower() == arg))
                    {
                        embedMessage = $"Роль **{arg}** не существует на сервере.";
                    }

                    else if (targetRole == null)
                    {
                        embedMessage = $"У {ctx.Member.Mention} отсутствует роль **{arg}**.";
                    }
                    else
                    {
                        await (ctx.User as DiscordMember).RevokeRoleAsync(targetRole);
                        embedMessage = $"{ctx.Member.Mention} снял с себя роль **{arg}**.";
                    }
                    embed = new DiscordEmbedBuilder()
                    {
                        Description = embedMessage,
                        Color = new DiscordColor("#425684")
                    };
                    await ctx.Client.SendMessageAsync(ctx.Channel, embed: embed);
                }
                catch (Exception e)
                {
                    ctx.Client.DebugLogger.LogMessage(LogLevel.Error, "002", $"Exception occured: {e.GetType()}: {e.Message}", DateTime.Now);
                }
            }
        }
    }
}
