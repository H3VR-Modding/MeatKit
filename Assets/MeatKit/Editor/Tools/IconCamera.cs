#if UNITY_EDITOR

using System.IO;
using UnityEngine;
using UnityEditor;
 
public class IconCamera : MonoBehaviour
{
    [Header("Hover over variables to show additional tooltips.")]
    [Tooltip("Use this to trigger the capture of an icon IN PLAYMODE ONLY!")]
    public KeyCode iconCaptureKey;
    [Tooltip("Use this checkbox like a button to trigger the capture of an icon (Editor or Playmode).")]
    public bool iconCaptureButton;
    [Tooltip("Path to icons folder (without \"Assets\" in the beginning).")]
    public string path = "Icons";
    [Tooltip("Name of the generated Icon without a file extension (no \".png\" required).")]
    public string iconName = "ExampleIcon";
    [Tooltip("Reference RenderTexture used by the camera.")]
    public RenderTexture renderTexture;

    private Camera thisCamera
    {
        get
        {
            if (!_camera)
            {
                _camera = this.gameObject.GetComponent<Camera>();
            }
            return _camera;
        }
    }
    private Camera _camera;
 
    private void LateUpdate()
    {
        if (Input.GetKeyDown(iconCaptureKey))
        {
            Capture();
        }
    }
 
	private void OnValidate()
	{
		if (iconCaptureButton)
		{
		    Capture();
			iconCaptureButton = false;
		}
	}
    public void Capture()
    {
        RenderTexture activeRenderTexture = RenderTexture.active;
        thisCamera.targetTexture = renderTexture;
        RenderTexture.active = thisCamera.targetTexture;
 
        thisCamera.Render();
 
        Texture2D image = new Texture2D(thisCamera.targetTexture.width, thisCamera.targetTexture.height);
        image.ReadPixels(new Rect(0, 0, thisCamera.targetTexture.width, thisCamera.targetTexture.height), 0, 0);
        image.Apply();
        RenderTexture.active = activeRenderTexture;
 
        byte[] bytes = image.EncodeToPNG();

        if (Application.isPlaying) Destroy(image);
        else DestroyImmediate(image);

        thisCamera.targetTexture = null;
        File.WriteAllBytes(Application.dataPath + "/" + path + "/" + iconName + ".png", bytes);

        AssetDatabase.ImportAsset("Assets" + "/" + path + "/" + iconName + ".png", ImportAssetOptions.ForceUpdate);
        //AssetDatabase.Refresh();

        TextureImporter importer = (TextureImporter)TextureImporter.GetAtPath("Assets" + "/" + path + "/" + iconName + ".png");

        importer.isReadable = true;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;

        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
        AssetDatabase.Refresh();
    }
}
#endif