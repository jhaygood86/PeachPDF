# PeachReader iOS and Mac Catalyst heads

One UIKit view (`Platforms/Apple`) serves both heads: a `UIScrollView` over a content view with a `CALayer` sheet per visible page and 1024 px
tile layers painted through `CoreGraphicsCanvas` (three per pass, LRU of 24).

Found by running it:
- The MAUI SDK drops every `Platforms/<x>` folder not named for the head being built (and an explicit `Compile Include` collides with the default
  glob at evaluation time), so the shared folder is added by a target `BeforeTargets="CoreCompile"`.
- SDK 27 requires a scene manifest. The config name must be `__MAUI_DEFAULT_SCENE_CONFIGURATION__` with a `MauiUISceneDelegate` subclass; with any other
  name the window opens blank. `connectionOptions.UrlContexts` can be null.
- Restore needs the workload of every TFM, so on a Mac the Android TFM is left out unless `-p:ReaderIncludeAndroid=true`.
- Minimums: iOS 16 (`UIEditMenuInteraction`), Mac Catalyst 17 (SDK floor).
- Not done: live pinch preview, hand cursor over links, menu bar and keyboard shortcuts on Catalyst. iOS simulator run was not performed; only the Catalyst app was run (`PEACH_SMOKE` line and screenshot).
