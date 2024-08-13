# Store-DuelDice
Duel Dice Module for Store: Challenge other players to a dice duel and place bets. Duel commands, bet limits, and cooldowns are configurable

# Config
Config will be auto generated. Default:
```json
{
  "duel_commands": [
    "dueldice",
    "diceduel"
  ],
  "acceptduel_commands": [
    "acceptdueldice",
    "acceptdiceduel"
  ],
  "refuseduel_commands": [
    "refusedueldice",
    "refusediceduel"
  ],
  "challenge_cooldown": 10, // // Cooldown time for challenging again (in seconds)
  "accept_timeout": 60, // Time allowed for the challenged player to accept the duel (in seconds)
  "max_dice_value": 6, // Maximum value of the dice
  "min_bet": 10, // Minimum bet amount
  "max_bet": 1000 // Maximum bet amount
}
```
