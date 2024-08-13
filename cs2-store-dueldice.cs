using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Commands.Targeting;
using StoreApi;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace Store_DuelDice;

public class Store_DuelDiceConfig : BasePluginConfig
{
    [JsonPropertyName("duel_commands")]
    public List<string> DuelCommands { get; set; } = ["dueldice", "diceduel"];

    [JsonPropertyName("acceptduel_commands")]
    public List<string> AcceptDuelCommands { get; set; } = ["acceptdueldice", "acceptdiceduel"];

    [JsonPropertyName("refuseduel_commands")]
    public List<string> RefuseDuelCommands { get; set; } = ["refusedueldice", "refusediceduel"];

    [JsonPropertyName("challenge_cooldown")]
    public int ChallengeCooldown { get; set; } = 10;

    [JsonPropertyName("accept_timeout")]
    public int AcceptTimeout { get; set; } = 60;

    [JsonPropertyName("max_dice_value")]
    public int MaxDiceValue { get; set; } = 6;

    [JsonPropertyName("min_bet")]
    public int MinBet { get; set; } = 10;

    [JsonPropertyName("max_bet")]
    public int MaxBet { get; set; } = 1000;
}

public class DuelRequest
{
    public CCSPlayerController Challenger { get; set; }
    public CCSPlayerController Opponent { get; set; }
    public int BetCredits { get; set; }
    public DateTime RequestTime { get; set; }

    public DuelRequest(CCSPlayerController challenger, CCSPlayerController opponent, int betCredits)
    {
        Challenger = challenger;
        Opponent = opponent;
        BetCredits = betCredits;
        RequestTime = DateTime.Now;
    }
}

public class Store_DuelDice : BasePlugin, IPluginConfig<Store_DuelDiceConfig>
{
    public override string ModuleName => "Store Module [Duel Dice]";
    public override string ModuleVersion => "0.0.1";
    public override string ModuleAuthor => "Nathy";

    private readonly Random random = new();
    public IStoreApi? StoreApi { get; set; }
    public Store_DuelDiceConfig Config { get; set; } = new();
    private readonly ConcurrentDictionary<string, DuelRequest> pendingDuels = new();
    private readonly ConcurrentDictionary<string, DateTime> playerLastChallengeTimes = new();

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        StoreApi = IStoreApi.Capability.Get() ?? throw new Exception("StoreApi could not be located.");
        CreateCommands();
    }

    public void OnConfigParsed(Store_DuelDiceConfig config)
    {
        Config = config;
    }

    private void CreateCommands()
    {
        foreach (var cmd in Config.DuelCommands)
        {
            AddCommand($"css_{cmd}", "Challenge a player to a dice duel", Command_DuelDice);
        }
        foreach (var cmd in Config.AcceptDuelCommands)
        {
            AddCommand($"css_{cmd}", "Accept a dice duel challenge", Command_AcceptDuel);
        }
        foreach (var cmd in Config.RefuseDuelCommands)
        {
            AddCommand($"css_{cmd}", "Refuse a dice duel challenge", Command_RefuseDuel);
        }
    }

    [CommandHelper(minArgs: 2, usage: "<player> <credits>")]
    public void Command_DuelDice(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;

        if (StoreApi == null) throw new Exception("StoreApi could not be located.");

        if (playerLastChallengeTimes.TryGetValue(player.SteamID.ToString(), out var lastChallengeTime))
        {
            var cooldownRemaining = (DateTime.Now - lastChallengeTime).TotalSeconds;
            if (cooldownRemaining < Config.ChallengeCooldown)
            {
                var secondsRemaining = (int)(Config.ChallengeCooldown - cooldownRemaining);
                info.ReplyToCommand(Localizer["Prefix"] + Localizer["Challenge cooldown", secondsRemaining]);
                return;
            }
        }

        playerLastChallengeTimes[player.SteamID.ToString()] = DateTime.Now;

        var targetResult = info.GetArgTargetResult(1);
        var opponent = targetResult.Players.FirstOrDefault();

        if (opponent == null)
        {
            info.ReplyToCommand(Localizer["Prefix"] + Localizer["Player not found"]);
            return;
        }

        if (!int.TryParse(info.GetArg(2), out int credits) || credits <= 0)
        {
            info.ReplyToCommand(Localizer["Prefix"] + Localizer["Invalid amount of credits"]);
            return;
        }

        if (credits < Config.MinBet)
        {
            info.ReplyToCommand(Localizer["Prefix"] + Localizer["Minimum bet amount", Config.MinBet]);
            return;
        }

        if (credits > Config.MaxBet)
        {
            info.ReplyToCommand(Localizer["Prefix"] + Localizer["Maximum bet amount", Config.MaxBet]);
            return;
        }

        if (StoreApi.GetPlayerCredits(player) < credits)
        {
            info.ReplyToCommand(Localizer["Prefix"] + Localizer["Not enough credits"]);
            return;
        }

        var duelRequest = new DuelRequest(player, opponent, credits);
        pendingDuels[opponent.SteamID.ToString()] = duelRequest;
        opponent.PrintToChat(Localizer["Prefix"] + Localizer["Duel request", player.PlayerName, credits]);
        player.PrintToChat(Localizer["Prefix"] + Localizer["You send a duel", opponent.PlayerName, credits]);

        AddTimer(Config.AcceptTimeout, () =>
        {
            if (pendingDuels.TryRemove(opponent.SteamID.ToString(), out var duel))
            {
                duel.Challenger.PrintToChat(Localizer["Prefix"] + Localizer["Duel timeout"]);
            }
        });
    }

    public void Command_AcceptDuel(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;

        if (StoreApi == null) throw new Exception("StoreApi could not be located.");

        if (!pendingDuels.TryGetValue(player.SteamID.ToString(), out var duel))
        {
            info.ReplyToCommand(Localizer["Prefix"] + Localizer["No pending duel"]);
            return;
        }

        if (StoreApi.GetPlayerCredits(player) < duel.BetCredits)
        {
            info.ReplyToCommand(Localizer["Prefix"] + Localizer["Not enough credits"]);
            return;
        }

        int challengerRoll = random.Next(1, Config.MaxDiceValue + 1);
        int opponentRoll = random.Next(1, Config.MaxDiceValue + 1);

        var winner = challengerRoll > opponentRoll ? duel.Challenger : (challengerRoll < opponentRoll ? duel.Opponent : null);
        var loser = winner == duel.Challenger ? duel.Opponent : (winner == duel.Opponent ? duel.Challenger : null);

        if (winner != null)
        {
            StoreApi.GivePlayerCredits(winner, duel.BetCredits);
            StoreApi.GivePlayerCredits(loser, -duel.BetCredits);

            duel.Challenger.PrintToChat(Localizer["Prefix"] + Localizer["Duel roll 1", duel.Challenger.PlayerName, challengerRoll]);
            duel.Opponent.PrintToChat(Localizer["Prefix"] + Localizer["Duel roll 1", duel.Challenger.PlayerName, challengerRoll]);

            duel.Opponent.PrintToChat(Localizer["Prefix"] + Localizer["Duel roll 2", duel.Opponent.PlayerName, opponentRoll]);
            duel.Challenger.PrintToChat(Localizer["Prefix"] + Localizer["Duel roll 2", duel.Opponent.PlayerName, opponentRoll]);

            duel.Challenger.PrintToChat(Localizer["Prefix"] + Localizer["Duel result", winner.PlayerName, duel.BetCredits]);
            duel.Opponent.PrintToChat(Localizer["Prefix"] + Localizer["Duel result", winner.PlayerName, duel.BetCredits]);
        }
        else
        {
            duel.Challenger.PrintToChat(Localizer["Prefix"] + Localizer["Duel roll 1", duel.Challenger.PlayerName, challengerRoll]);
            duel.Opponent.PrintToChat(Localizer["Prefix"] + Localizer["Duel roll 1", duel.Challenger.PlayerName, challengerRoll]);

            duel.Opponent.PrintToChat(Localizer["Prefix"] + Localizer["Duel roll 2", duel.Opponent.PlayerName, opponentRoll]);
            duel.Challenger.PrintToChat(Localizer["Prefix"] + Localizer["Duel roll 2", duel.Opponent.PlayerName, opponentRoll]);

            duel.Challenger.PrintToChat(Localizer["Prefix"] + Localizer["Duel tie"]);
            duel.Opponent.PrintToChat(Localizer["Prefix"] + Localizer["Duel tie"]);
        }

        pendingDuels.TryRemove(player.SteamID.ToString(), out _);
    }

    public void Command_RefuseDuel(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;

        if (StoreApi == null) throw new Exception("StoreApi could not be located.");

        if (!pendingDuels.TryGetValue(player.SteamID.ToString(), out var duel))
        {
            info.ReplyToCommand(Localizer["Prefix"] + Localizer["No pending duel"]);
            return;
        }

        duel.Challenger.PrintToChat(Localizer["Prefix"] + Localizer["Duel refused", player.PlayerName]);

        pendingDuels.TryRemove(player.SteamID.ToString(), out _);
    }
}