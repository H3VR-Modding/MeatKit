#if H3VR_IMPORTED
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;

/*
 * SUPER LARGE WARNING ABOUT THIS CLASS
 * This is the default and fallback class that MeatKit uses as a template to generate a BepInEx plugin
 * when building your mod. DO NOT MODIFY THIS FILE AT ALL, IN ANY WAY.
 *
 * If you want to add custom behavior to your mod, you should make a copy of this class, and put it inside
 * the main namespace of your mod (that namespace can be found by opening the 'Allowed Namespaces' list on your build
 * profile). MeatKit will then detect and use that class instead of this one, for that one specific profile.
 *
 * HOWEVER, YOU MUST KEEP ALL OF THE STUFF FROM THIS TEMPLATE, otherwise MeatKit may fail to correctly build
 * your plugin, or your mod may fail to correctly load.
 */

// DO NOT REMOVE OR CHANGE ANY OF THESE ATTRIBUTES
[BepInPlugin("MeatKit", "MeatKit Plugin", "1.0.0")]
[BepInProcess("h3vr.exe")]

// DO NOT CHANGE THE NAME OF THIS CLASS OR THE BASE CLASS. If you're making a custom plugin, make sure it extends BaseUnityPlugin.
public class MeatKitPlugin : BaseUnityPlugin
{
    // DO NOT CHANGE OR REMOVE THIS FIELD.
#pragma warning disable 414
    private static readonly string BasePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    internal new static ManualLogSource Logger;
#pragma warning restore 414

    // You are free to edit this method, however please ensure LoadAssets is still called somewhere inside it.
    private void Awake()
    {
        // This lets you use your BepInEx-provided logger from other scripts in your project
        Logger = base.Logger;
        
        // You may place code before/after this, but do not remove this call to LoadAssets
        LoadAssets();
    }

    // DO NOT CHANGE OR REMOVE THIS METHOD. It's contents will be overwritten when building your package.
    private void LoadAssets()
    {
        // Code to load your build items will be generated at build-time and inserted here
    }
}
#endif
