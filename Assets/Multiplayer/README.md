# Quest 2 multiplayer: first version

## What is implemented

Two players can occupy the casino in `Assets/Scenes/SampleScene.unity`. One headset hosts and the other joins using the host's IPv4 address. No cloud account or paid service is required. The runtime bootstrap installs the lobby automatically in SampleScene; you do not need to modify the scene or add a second XR rig.

Each player sees their own simple blue hands and the other player's orange head and hands. Controller transforms drive the hands (this is not optical hand tracking or finger tracking). The local head is hidden. Controller model renderers named UniversalController are hidden while the simple hands are active; controller interactions and rays remain enabled. Remote poses are sent at 20 Hz using unreliable sequenced messages and smoothed locally; teleport-sized changes snap into place. The guest starts 1.2 metres beside the rig's initial position.

This is player-presence multiplayer. Roulette, slot machines, balances, chips, beer pong, grabbed objects, and physics remain local on each headset. Voice chat and internet matchmaking are not included. Both devices must run the same build and scene.

## Build and connect

1. Open the project in **Unity 6000.6.0f1** and let Package Manager finish importing Netcode for GameObjects **2.13.3**. The project resolves Unity Transport through its package dependencies.
2. Open **Assets/Scenes/SampleScene.unity**. Run **Tools > Multiplayer > Prepare and Validate**. This checks pose serialization/invalid-pose rejection, ensures the avatar material exists, and enables Android Internet permission.
3. Build the same Android APK for both Quest 2 headsets. Keep SampleScene enabled as the startup scene in the active Build Profile. The existing project uses ARM64 and OpenXR; retain the existing Quest configuration.
4. Connect both headsets to the same private Wi-Fi router or phone hotspot. Campus/student Wi-Fi may isolate devices even if both successfully log in. A hotspot must also allow connected devices to communicate with each other.
5. On headset A, point a controller at **HOST** and pull the trigger. Read the **Your IP** address shown on its panel. If multiple addresses appear, use the address associated with its Wi-Fi network; it is also available in the headset's Wi-Fi details.
6. On headset B, enter A's address with the panel keypad and press **JOIN**. Both panels should show **Connected — 2/2 players**.
7. Press **HIDE** to dismiss the panel. Press **Y on the left controller** to show/hide it again. The panel reappears in front of you; it does not follow your head continuously. Keyboard **M** toggles it in the Editor.
8. **LEAVE** disconnects. When the guest leaves, the host stays available for another guest. When the host leaves, the guest disconnects. Pausing/removing a headset disconnects its session; use Host/Join again after returning.

If you cannot connect, check the address, both devices' Wi-Fi, and client isolation. UDP port **7777** must be reachable. For Editor-to-headset tests, the computer must be on the same reachable LAN and the firewall must allow Unity traffic on that private network. Do not enter 127.0.0.1 on a headset to reach another device. Joining times out after 15 seconds with an on-screen explanation. Different homes/networks require a later Relay or other online transport integration.

## Two-headset acceptance test (still required)

- Launch offline: own hands are visible and the original grabbing/teleport/locomotion still work.
- Host/join: both report 2/2; each sees exactly one remote head and pair of hands, but no local head.
- Wave each hand, crouch, turn, walk, use joystick locomotion, and teleport. Confirm the other headset sees the motion at the same casino locations.
- Hide/reopen the panel with Y, and select all controls using controller rays.
- Attempt a third connection: it receives Lobby full and does not disturb the existing two players.
- Guest leaves and rejoins; host leaves and restarts; guest retries. Confirm remote avatars are removed and recreated without duplication.
- Enter an invalid IP and an unreachable IP; verify useful errors and retry controls.
- Put a headset to sleep, wake it, and reconnect. No stale avatar should remain indefinitely.

## Code and extension points

- `Runtime/CasinoMultiplayer.cs`: SampleScene bootstrap, XR rig lookup, two-player approval, host/join/disconnect lifecycle, pose transmission and reception.
- `Runtime/AvatarPose.cs`: explicitly serialized 84-byte pose payload with finite position/valid rotation checks.
- `Runtime/SimpleAvatar.cs`: collider-free procedural palm/finger/head meshes, local head hiding and remote smoothing.
- `Runtime/LobbyPanel.cs`: world-space XR UI, IP keypad and Y/M toggle.
- `Editor/MultiplayerSetup.cs`: setup and serialization validation menu.
- `Assets/Resources/Multiplayer/Avatar.mat`: referenced URP material, included in player builds to avoid runtime shader stripping.

The network has no player prefab: it exchanges presence poses while keeping the existing local XR Origin. To synchronize casino gameplay next, introduce server-authoritative state and network identities for the games and shared objects. Do not simply broadcast local balances or competing physics simulations.

## Validation performed

Unity's bundled Roslyn compiler compiled Unity Transport, Netcode 2.13.3, and all added runtime/editor scripts against this project's Unity 6.6 libraries. Package JSON and change whitespace were checked. Full Unity batch validation exited during licensing/system initialization before compilation, so the editor validation menu, APK build, rendering, UI interaction, and two-device networking have **not** been run successfully here. The steps above are the remaining device acceptance checks.

Reference: [Unity's XR UI setup example](https://github.com/Unity-Technologies/XR-Interaction-Toolkit-Examples/blob/main/Documentation/UI-2D.md) describes the world-space canvas, tracked-device raycaster and XR UI input module used by the lobby.
