# Arcane Shift: Lucky Brawlers

A 1-week multiplayer game jam project developed as a term project for the **Game Development (CENG462)** course at Ankara Yıldırım Beyazıt University.

## About

Itch.io Page: https://eiziiaizii11.itch.io/arcane-brawlers-lucky-shift 

Arcane Shift: Lucky Brawlers is a real-time online multiplayer brawler built in Unity. Players take on the role of wizards competing against each other in fast-paced arena combat. A global Lucky Box event system periodically shakes up the match, rewarding (or punishing) everyone on the field.

The project was built entirely within a 1-week game jam timeline by a 3-person team.

## Team

- Aziz Önder
- İsmet Gökay
- Efe Baştuğ

## Features

- Online multiplayer with lobby creation and matchmaking
- Real-time relay-based networking (no dedicated server required)
- Lucky Box global event system affecting all players simultaneously
- Wizard and slime form transformations with procedural animation
- Real-time in-game leaderboard

## Tech Stack

| Technology | Purpose |
|---|---|
| Unity | Game engine |
| Netcode for GameObjects | Server-authoritative multiplayer networking |
| Unity Lobby (UGS) | Lobby creation and matchmaking |
| Unity Relay (UGS) | Peer connection without a dedicated server |
| Unity Authentication (UGS) | Anonymous player identity |

## How to Play

1. Launch the game
2. Create or join a lobby
3. Survive, brawl, and watch out for Lucky Box events

## Running the Project

1. Clone the repository
2. Open the project in Unity (2022.3 LTS or newer recommended)
3. Make sure the Unity Gaming Services project ID is configured under **Edit > Project Settings > Services**
4. Open the main scene and hit Play

## Course Context

This project was developed as the term project for the **Game Development (CENG462)** course. The game jam format was a 1-week sprint to design, build, and deliver a functional multiplayer game from scratch.
