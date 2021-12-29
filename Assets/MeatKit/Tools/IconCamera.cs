using System.IO;
using UnityEngine;
using UnityEditor;
 
[ExecuteInEditMode]
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

    [Tooltip("The depth texture mode of the camera. Some effects will require either DepthNormals or Depth to work correctly")]
    public DepthTextureMode CameraDepthMode = DepthTextureMode.DepthNormals;

    [Tooltip("Material that determines the post effect of the image")]
    public Material effectMaterial;

    [Tooltip("The background image that will be applied in areas with full transparency")]
    public Texture2D background;

    [HideInInspector]
    public RenderTexture renderTexture;


    public Camera thisCamera
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
 

    private void Update()
    {
        if (Input.GetKeyDown(iconCaptureKey))
        {
            Capture();
        }
    }
 

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if(renderTexture != null)
        {
            RenderTexture.ReleaseTemporary(renderTexture);
        }
        
        if (effectMaterial != null)
        {
            Graphics.Blit(source, destination, effectMaterial);
        }
        else
        {
            Graphics.Blit(source, destination);
        }

        renderTexture = RenderTexture.GetTemporary(source.width, source.height);
        Graphics.Blit(destination, renderTexture);
    }

    public void Capture()
    {
        Debug.Log("Say Cheese!");

        //Manually calling render on the camera caused editor lockup with larger resolutions
        //thisCamera.Render();

        RenderTexture.active = renderTexture;
        Texture2D texture = new Texture2D(renderTexture.width, renderTexture.height);
        texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture.Apply();

        texture = FlipTexture(texture);

        if(background != null)
        {
            texture = AddBackground(texture, background);
        }
        
 
        byte[] bytes = texture.EncodeToPNG();

        string imagePath = Application.dataPath + "/" + path + "/" + iconName + ".png";
        string assetPath = "Assets" + "/" + path + "/" + iconName + ".png";
        File.WriteAllBytes(imagePath, bytes);

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);


        TextureImporter importer = (TextureImporter)TextureImporter.GetAtPath(assetPath);

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



    /// <summary>
    /// Returns a flipped copy of the given texture
    /// Taken from this forum: https://forum.unity.com/threads/flipping-texture2d-image-within-unity.35974/
    /// </summary>
    /// <param name="original"></param>
    /// <returns></returns>
    public Texture2D FlipTexture(Texture2D original)
    {
        Texture2D flipped = new Texture2D(original.width, original.height);

        int origWidth = original.width;
        int origHeight = original.height;

        for (int x = 0; x < origWidth; x++)
        {
            for (int y = 0; y < origHeight; y++)
            {
                flipped.SetPixel(x, origHeight - y - 1, original.GetPixel(x, y));
            }
        }
        flipped.Apply();

        return flipped;
    }


    public Texture2D AddBackground(Texture2D original, Texture2D background)
    {
        Texture2D result = new Texture2D(original.width, original.height);

        int origWidth = original.width;
        int origHeight = original.height;
        int backWidth = background.width;
        int backHeight = background.height;

        for (int x = 0; x < origWidth; x++)
        {
            for (int y = 0; y < origHeight; y++)
            {
                Color originalPixel = original.GetPixel(x, y);

                if(originalPixel.a == 0)
                {
                    int backX = (int)Remap(x, 0, origWidth, 0, backWidth);
                    int backY = (int)Remap(y, 0, origHeight, 0, backHeight);
                    result.SetPixel(x, y, background.GetPixel(backX, backY));
                }
                else
                {
                    result.SetPixel(x, y, originalPixel);
                }
            }
        }

        result.Apply();

        return result;
    }

    /// <summary>
    /// Maps a given value from one range to another
    /// Taken from this forum: https://forum.unity.com/threads/re-map-a-number-from-one-range-to-another.119437/
    /// </summary>
    /// <param name="value"></param>
    /// <param name="from1"></param>
    /// <param name="to1"></param>
    /// <param name="from2"></param>
    /// <param name="to2"></param>
    /// <returns></returns>
    public float Remap(float value, float from1, float to1, float from2, float to2)
    {
        return (value - from1) / (to1 - from1) * (to2 - from2) + from2;
    }


}