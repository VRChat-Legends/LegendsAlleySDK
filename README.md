<div align="center">

<img src="Packages/com.legendsnexus.alley-sdk/Editor/Window/alley-logo.png" alt="Legends Alley" width="340" />

# Legends Alley SDK

Build, check, and upload your community booth for Legends Alley, straight from Unity.

[![Release](https://img.shields.io/github/v/release/VRChat-Legends/LegendsAlleySDK?label=release&color=FF007A)](https://github.com/VRChat-Legends/LegendsAlleySDK/releases)
[![Unity](https://img.shields.io/badge/Unity-2022.3-1FD1ED?logo=unity)](https://unity.com/releases/editor/whats-new/2022.3.22)
[![VRChat Worlds SDK](https://img.shields.io/badge/VRChat%20Worlds%20SDK-3.7%2B-6B46C1)](https://creators.vrchat.com/worlds/)
[![VPM Listing](https://img.shields.io/badge/VPM-vrchatlegends.com-FFD700)](https://vrchatlegends.com/vpm/index.json)
[![Discord](https://img.shields.io/badge/Discord-VRChat%20Legends-5865F2?logo=discord&logoColor=white)](https://discord.gg/6xPkZ7Dxp9)

</div>

Legends Alley is a booth event by [VRChat Legends](https://vrchatlegends.com). Every approved community gets a plot in the event world and fills it with their own booth. This SDK is how you build that booth: it checks your work against the event limits as you go, then packages and uploads it without you ever leaving the editor.

## What you get

- **The SDK window**: sign in with Discord, see the current event and its deadline, check your booth against the live limits, and upload when everything is green.
- **A booth kit**: drop-in prefabs that already work in game, no scripting needed.
  - **Group Button**: a round button that opens your VRChat group page so visitors can join on the spot.
  - **Avatar Pedestal**: a compact clickable avatar picture that switches whoever presses it into your avatar.
  - **Video Player**: a booth-sized screen with play, pause, and volume controls. Plays YouTube links, direct video files, and live streams. It only runs for people standing near your booth, so fifty booths can each have one without melting anybody's frames.
- **A booth optimizer**: one click combines your meshes and atlases your textures down to a handful of materials, and it knows to leave the interactive stuff alone.
- **Live validation**: triangle counts, texture memory, draw calls, audio range, shader rules, all checked in the editor with plain-language hints instead of cryptic errors.

## Requirements

| | |
|---|---|
| Unity | 2022.3 (the VRChat Creator Companion installs the right version for you) |
| VRChat SDK | Worlds 3.7.0 or newer |
| Account | The Discord account that owns an approved Legends Alley community |

Don't have an approved community yet? [Apply here](https://vrchatlegends.com/alley/apply) first. Approval is per community, and the Discord account that applied becomes the one that can sign in and upload.

## Installing with the Creator Companion

1. Install the [VRChat Creator Companion](https://vcc.docs.vrchat.com/) if you don't have it yet.
2. In the Creator Companion, open **Settings**, then **Packages**, then **Add Repository**.
3. Paste this listing URL and confirm:

   ```
   https://vrchatlegends.com/vpm/index.json
   ```

4. Open (or create) your **Worlds** project, click **Manage Project**, find **Legends Alley SDK** in the package list, and add it.
5. In Unity, open the window from the menu bar: **Legends Alley > SDK Window**.

That's it. Updates show up in the Creator Companion like any other VRChat package.

<details>
<summary>Installing without the Creator Companion (manual)</summary>

Grab the latest `com.legendsnexus.alley-sdk` zip from the [releases page](https://github.com/VRChat-Legends/LegendsAlleySDK/releases), unzip it into your project's `Packages` folder, and let Unity import. You'll need the VRChat Worlds SDK in the project already. The Creator Companion route is strongly recommended since it handles updates for you.

</details>

## Building your booth

1. **Sign in.** Open **Legends Alley > SDK Window** and press SIGN IN WITH DISCORD. Your browser handles the rest. Use the account that owns your community.
2. **Make the booth.** Create an empty object in your scene, add the **Legends Booth** component to it (Add Component, then Legends Alley, then Legends Booth), and build everything as children of that object.
3. **Mind the front.** The pink arrow gizmo shows which way your booth faces. Visitors approach from that side, so put your good stuff there.
4. **Check it.** The BOOTH tab shows every limit for the current event with your numbers next to them. Anything over the line comes with a hint about how to fix it, and clicking a row selects the objects responsible.
5. **Upload.** Press BUILD + UPLOAD. The SDK packages a copy of your booth (your scene is never touched), uploads it, and the server double checks everything. Uploading again later just replaces your previous version.

## The booth kit

All three prefabs live under **GameObject > Legends Alley** and each one has a friendly inspector that tells you exactly what it needs.

### Group Button

Spawn it, paste your group ID into the inspector (it looks like `grp_12345678-1234-1234-1234-123456789abc`, copy it from your group page's address bar on the VRChat website), done. In game, pressing the button opens your group page so people can join right there.

### Avatar Pedestal

Paste your avatar ID (`avtr_...`, from the avatar's page on the VRChat website) and keep the avatar set to public. In game the avatar's picture appears right where the gizmo shows it, and pressing it switches people into your avatar. The picture is drawn by VRChat itself, so it won't show in the editor, only in game.

### Video Player

Paste a link and you're done. It handles:

- **YouTube videos**: paste the normal watch link
- **Direct video files**: any `https://` link ending in a video file, `.mp4` works best
- **Live streams**: stream links like VRCDN work out of the box

The player is range based. It starts on its own when someone walks within the play range you set (3 to 5 meters) and stops when they leave, so it never fights other booths for attention or bandwidth. Visitors get a play and pause button, a volume slider, and a status readout. Audio is hard capped at 5 meters so your sound stays inside your booth.

## Staying under the limits

- The BOOTH tab is the source of truth: it always shows the limits for the event you're uploading to.
- The TOOLS tab has the **booth optimizer**. Point it at your booth and it combines your meshes into one and packs your textures into shared atlases, usually the single biggest win for draw calls and material counts. It works on a copy and leaves your original disabled next to it, so you can always go back.
- Interactive things (the booth kit prefabs, pickups, anything with Udon on it) pass through the optimizer untouched, so it's safe to run on a finished booth.
- ProBuilder geometry is welcome. It gets combined and atlased automatically at upload time.
- Shaders are limited to an event whitelist: Standard, z3y, Filamented, Poiyomi (not Pro), lilToon, unlit, legacy, TMP, UI, and particle shaders. The checker names any material that's off the list.

## FAQ

<details>
<summary><b>Do I need to know how to script or use Udon?</b></summary>

No. The booth kit prefabs cover the interactive stuff and everything else is regular Unity building. If you do bring your own Udon, the event limits cap how much of it a booth can carry, and the checker will tell you where you stand.

</details>

<details>
<summary><b>Can my teammates upload the booth?</b></summary>

Only the Discord account that owns the approved community can sign in and upload. Anyone can help build the booth in Unity, but the final upload goes through the owner.

</details>

<details>
<summary><b>How do I update my booth after uploading?</b></summary>

Just upload again. Each upload replaces your previous booth for that event, and staff sync the newest version into the event world.

</details>

<details>
<summary><b>Which way does my booth face?</b></summary>

Toward the pink arrow on the Legends Booth gizmo (the local forward of your booth root). Plots in the event world are rotated so that arrow points at the walkway.

</details>

<details>
<summary><b>Why doesn't the video player play in the editor?</b></summary>

The video engine only runs in VRChat itself, so in the editor the screen stays dark. The controls, range behavior, and status text are all still wired up correctly, and it plays once you're in game. Also worth knowing: visitors who have "Allow Untrusted URLs" turned off in their VRChat settings won't get videos from most links, and the status text on the player tells them so.

</details>

<details>
<summary><b>Why does the video only start when I walk up to it?</b></summary>

On purpose. Videos load and play per visitor, only for people within your configured play range. That's what makes it safe for every booth in the hall to have a screen: nobody's client tries to stream fifty videos at once.

</details>

<details>
<summary><b>The avatar pedestal is invisible in the editor. Is it broken?</b></summary>

It's fine. VRChat draws the avatar picture in game, the editor can't render it. The purple gizmo box shows exactly where the picture will appear and how big it will be.

</details>

<details>
<summary><b>My booth is over the triangle or material limit. Now what?</b></summary>

Open the TOOLS tab and run the booth optimizer, it usually solves material and draw call problems in one go. For triangles, look at the checker's offender list (clicking the row selects the heaviest objects) and simplify those meshes. For texture memory, shrink textures that don't need to be 4K, most props read fine at 512 or 1024.

</details>

<details>
<summary><b>A shader I use got flagged. Why?</b></summary>

Booths from dozens of communities share one world, so shaders are limited to a whitelist the event world is known to handle well. Swap flagged materials to Standard or any listed family. If you think a shader deserves to be on the list, ask in the Discord.

</details>

<details>
<summary><b>Does uploading change my scene?</b></summary>

No. The SDK duplicates your booth, does all its preparation on the copy (lighting cleanup, audio caps, packaging), uploads, and deletes the copy. Your scene stays exactly as you built it.

</details>

<details>
<summary><b>Sign in opens the browser but never finishes.</b></summary>

Make sure you approve the Discord prompt with the right account, and that nothing is blocking `127.0.0.1` loopback connections (some aggressive firewalls do). Then try again from the SDK window. If it keeps failing, grab us in the Discord.

</details>

## Need help?

Join the [VRChat Legends Discord](https://discord.gg/6xPkZ7Dxp9) and ask in the event channels. Bug reports and weird edge cases are welcome, screenshots of the SDK window's checker output help a lot.

