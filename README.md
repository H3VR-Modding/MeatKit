# MeatKit
MeatKit is a Unity Editor modding toolkit for developing and packaging custom maps, guns, and mods for Hot Dogs, Horseshoes & Handgrenades.

## Features
* Easily import the game's scripts to use in your mod.
* One click export to build your Thunderstore package.
* Sample scenes and prefabs for custom mods.

## Setup
To use this project, you must have a copy of the game and [Unity Editor 5.6.3p4](https://unity3d.com/unity/qa/patch-releases/5.6.3p4) installed. 
1. Download the `Source code (zip)` from the [latest release](https://github.com/H3VR-Modding/MeatKit/releases/latest), extract it, and open the project in Unity.
2. With the project open, select `MeatKit > Scripts > Import Game` on the menu bar at the top and point it to your `h3vr/h3vr_Data/Managed` folder.
3. After the game's scripts finish importing, navigate to the `Assets/MeatKit/` folder and right click > reimport the `Managed` folder.

> ⚠️ Note: If you are getting compile errors for missing the game's assembly and / or cannot see the MeatKit menu bar item, go to `Edit > Project Settings > Player` and in the `Other Settings` tab, clear the `Scripting Define Symbols` field and hit enter, then re-import the game's scripts.

## Usage
First, select `MeatKit > Build > Configure` from the menu bar.
This will select the build settings object and let you edit your mod's name,
version, description, along with other Thunderstore files such as icon and
readme. Additionally, this is where you add your 'build items', which tell
MeatKit what exactly you want to export from your project into your final build.

There are already some sample scenes / prefabs and build items ready to go in
the `Assets/Samples` folder, so to do a build of your mod and generate a Thunderstore
package all you have to do is select `MeatKit > Build > Build` from the menu bar.
The process might take a minute and when completed it will have built your mod
into the `AssetBundles` folder of your project.

Once you have your built package, you will need to import it into your R2MM profile.
You can do this by selecting all the files in the `AssetBundles` folder then right
clicking and selecting `Send To > Compressed (zipped) folder`. Finally in R2MM's
settings select `import local mod` and select that new zip file to import your mod.
(Note that after this first initial import, you may instead just copy and overwrite
the files in your profile folder if you know how.)

## Contributing
If you'd like to help develop MeatKit give me a shout on Discord `@nrgill28#9007`. I'd appreciate having a chance to talk about any changes you plan on making before you make a PR.
