
#if UNITY_EDITOR
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace MeatKit
{
    [ExecuteInEditMode]
    public class AdvancedIconCamera : MonoBehaviour
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
        public List<Material> effectMaterials = new List<Material>();

        [Tooltip("The background image that will be applied in areas with full transparency")]
        public Texture2D background;

        [Tooltip("Current object to take an Icon of.")]
        public GameObject ObjectToIconize;

        [Tooltip("Use this list instead if you wanna make icons in bulk automatically.")]
        public GameObject[] ObjectsToIconize;

        [Tooltip("Scene objects to stay active during image creation.")]
        public GameObject[] SceneObjectsToStayActive;

        [Tooltip("Size added to actual object size. Adds padding at the edges of the icon.")]
        public float Oversize = 0.001f;

        [Tooltip("Distance the camera will be placed at from the object. Only used in orthographic camera mode to make sure no camera clipping happens.")]
        public float OrthographicCameraDistance = 2f;

        [Tooltip("If checked, it will move to the object before taking a picture.")]
        public bool MoveToObject = true;

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
        private Dictionary<GameObject, bool> rootGameObjects;
        private GameObject _temp = null;
        private GameObject _currentObject = null;
        private Vector3 _targetPos;

        private int _currentPicture = 0;
        private int _lastPicture = -1;
        private bool _takingPictures = false;
        private bool _takingSinglePicture = false;

        private Dictionary<byte[], string> _pictures;

        private void Update()
        {
            if (Input.GetKeyDown(iconCaptureKey))
            {
                CaptureAndSave();
            }
        }

        void OnPreRender()
        {
            if (_takingPictures)
            {
                Debug.Log("Say Cheese!");
                Debug.Log("ObjectToIconize: " + ObjectToIconize.name);

                //HideOtherSceneObjects();
            }
            
            if (_takingSinglePicture)
            {
                Debug.Log("Say Cheese!");
                //HideOtherSceneObjects();
            }
            
        }


        void OnPostRender()
        {
            if (_takingPictures)
            {
                //UnhideOtherSceneObjects();
                _currentPicture++;
                _takingPictures = false;
            }

            if (_takingSinglePicture)
            {
                UnhideOtherSceneObjects();
                _takingSinglePicture = false;
            }

            //_takingPictures = false;
            //_takingSinglePicture = false;
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (renderTexture != null)
            {
                RenderTexture.ReleaseTemporary(renderTexture);
            }

            //Handle rendering through the effect materials
            if (effectMaterials.Count > 1)
            {
                List<RenderTexture> renderTextures = new List<RenderTexture>();

                for (int i = 0; i < effectMaterials.Count; i++)
                {
                    RenderTexture tempDest = RenderTexture.GetTemporary(source.width, source.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

                    //If this is the first material, start from the source texture
                    if (i == 0)
                    {
                        Graphics.Blit(source, tempDest, effectMaterials[i]);
                    }

                    //If this is the last material, send the resulting texture to destination
                    else if (i == effectMaterials.Count - 1)
                    {
                        Graphics.Blit(renderTextures.Last(), destination, effectMaterials[i]);
                    }

                    //If this is inbetween, pass between temp textures
                    else
                    {
                        Graphics.Blit(renderTextures.Last(), tempDest, effectMaterials[i]);
                    }

                    renderTextures.Add(tempDest);
                }

                for (int i = 0; i < renderTextures.Count; i++)
                {
                    RenderTexture.ReleaseTemporary(renderTextures[i]);
                }

            }

            else if (effectMaterials.Count == 1)
            {
                Graphics.Blit(source, destination, effectMaterials[0]);
            }

            else
            {
                Graphics.Blit(source, destination);
            }

            renderTexture = RenderTexture.GetTemporary(source.width, source.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(destination, renderTexture);
        }

        public Sprite CaptureAndSave()
        {
            //Manually calling render on the camera caused editor lockup with larger resolutions
            //thisCamera.Render();
            HideOtherSceneObjects();
            _takingSinglePicture = true;

            RenderTexture.active = renderTexture;
            Texture2D texture = new Texture2D(renderTexture.width, renderTexture.height);
            texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            texture.Apply();

            texture = FlipTexture(texture);

            if (background != null)
            {
                texture = AddBackground(texture, background);
            }


            byte[] bytes = texture.EncodeToPNG();

            string imagePath;
            string assetPath;
            if (ObjectToIconize != null)
            {
                imagePath = Application.dataPath + "/" + path + "/" + ObjectToIconize.name + "_icon" + ".png";
                assetPath = "Assets" + "/" + path + "/" + ObjectToIconize.name + "_icon" + ".png";
            }
            else
            {
                imagePath = Application.dataPath + "/" + path + "/" + iconName + ".png";
                assetPath = "Assets" + "/" + path + "/" + iconName + ".png";
            }
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
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        void Capture()
        {
            _takingPictures = true;
            RenderTexture.active = renderTexture;
            Texture2D texture = new Texture2D(renderTexture.width, renderTexture.height);
            texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            texture.Apply();

            texture = FlipTexture(texture);

            if (background != null)
            {
                texture = AddBackground(texture, background);
            }


            byte[] bytes = texture.EncodeToPNG();

            _pictures.Add(bytes, _currentObject.name);


        }

        void SaveFile(byte[] bytes, string fileName)
        {
            string imagePath;
            string assetPath;

            imagePath = Application.dataPath + "/" + path + "/" + fileName + "_icon" + ".png";
            assetPath = "Assets" + "/" + path + "/" + fileName + "_icon" + ".png";

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


        public void Capture(GameObject objectToIconize)
        {
            _temp = ObjectToIconize;

            //ObjectToIconize = objectToIconize;
            _currentObject = objectToIconize;
            _targetPos = Vector3.zero;

            EditorApplication.update += TakePictureOfObject;
        }
        void TakePictureOfObject()
        {
            if (this.transform.position != _targetPos)
            {
                HideOtherSceneObjects(_currentObject);
                if (MoveToObject) GoToObject(_currentObject);
                else _targetPos = this.transform.position;
            }
            else if (this.transform.position == _targetPos)
            {
                Capture();
                EditorApplication.update -= TakePictureOfObject;
            }
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

                    if (originalPixel.a == 0)
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


        public void GoToObject(GameObject goToObject = null)
        { 
            if (ObjectToIconize == null && goToObject == null) return;
            if (goToObject == null) goToObject = ObjectToIconize;

            Bounds objectBounds = goToObject.GetBounds();
            thisCamera.transform.position = objectBounds.center - thisCamera.transform.forward * OrthographicCameraDistance;
            float distance;
            if (thisCamera.orthographic == true)
            {
                Vector3 extents = objectBounds.extents;
                //Debug.Log("Orig extents: " + extents.ToString("F4"));
                //extents = _camera.transform.rotation * extents;


                extents.x *= Mathf.Clamp01(Mathf.Abs(Mathf.Cos(_camera.transform.eulerAngles.y * Mathf.Deg2Rad)) + Mathf.Abs(Mathf.Sin(_camera.transform.eulerAngles.x * Mathf.Deg2Rad)));
                //extents.x *= Mathf.Abs(Mathf.Cos(_camera.transform.eulerAngles.y * Mathf.Deg2Rad));
                extents.y *= Mathf.Clamp01(Mathf.Abs(Mathf.Cos(_camera.transform.eulerAngles.x * Mathf.Deg2Rad)));
                //extents.z *= Mathf.Abs(Mathf.Sin(_camera.transform.eulerAngles.y * Mathf.Deg2Rad));
                extents.z *= Mathf.Clamp01(Mathf.Abs(Mathf.Sin(_camera.transform.eulerAngles.y * Mathf.Deg2Rad)) + Mathf.Abs(Mathf.Sin(_camera.transform.eulerAngles.x * Mathf.Deg2Rad)));
                int highestComponentIndex = extents.GetHighestComponentIndex();
                float highestComponent = extents[highestComponentIndex];



                //thisCamera.orthographicSize = Mathf.Abs(highestComponent) + Oversize;
                //thisCamera.orthographicSize = extents.magnitude + Oversize;
                Vector3 min = thisCamera.WorldToViewportPoint(objectBounds.min);
                int index = 0;
                if (1 - min.y > min.x) index = 1;

                //Debug.Log("Orig Min: " + min.ToString("F4"));
                do
                {
                    thisCamera.orthographicSize = thisCamera.orthographicSize * (index + min[index] * Mathf.Pow(-1f, index));
                    min = thisCamera.WorldToViewportPoint(objectBounds.min);
                    //Debug.Log(Mathf.Abs(1f - (index + min[index] * Mathf.Pow(-1f, index))));
                    //Debug.Log("New Min: " + thisCamera.WorldToViewportPoint(objectBounds.min).ToString("F4"));
                } while (Mathf.Abs(1f - (index + min[index] * Mathf.Pow(-1f, index))) > 0.001f);
                thisCamera.orthographicSize += Oversize;
                distance = OrthographicCameraDistance;
                
            }
            else
            {
                float FOV = thisCamera.fieldOfView;
                float size = objectBounds.extents.magnitude + Oversize;
                distance = size / Mathf.Tan(FOV * Mathf.Deg2Rad / 2f);
            }
            thisCamera.transform.position = objectBounds.center - thisCamera.transform.forward * distance;
            _targetPos = thisCamera.transform.position;
        }

        public void HideOtherSceneObjects(GameObject currentObject = null)
        {
            if (rootGameObjects == null) rootGameObjects = new Dictionary<GameObject, bool>();
            if (ObjectToIconize == null && currentObject == null) return;
            if (currentObject == null) currentObject = ObjectToIconize;
            Scene GameObjectScene = currentObject.scene;
            //GameObject[] rootGameObjectsArray = SceneManager.GetActiveScene().GetRootGameObjects();
            GameObject[] rootGameObjectsArray = GameObjectScene.GetRootGameObjects();

            rootGameObjects.Clear();

            foreach (var rootGameObject in rootGameObjectsArray)
            {
                if (rootGameObject == currentObject || rootGameObject == this.gameObject || SceneObjectsToStayActive.Contains(rootGameObject)) continue;
                rootGameObjects.Add(rootGameObject, rootGameObject.activeSelf);
                rootGameObject.SetActive(false);
            }
        }

        public void UnhideOtherSceneObjects()
        {
            if (rootGameObjects == null) return;
            foreach (var rootGameObject in rootGameObjects)
            {
                rootGameObject.Key.SetActive(rootGameObject.Value);
            }
        }

        public void TakeSinglePicture()
        {
            if (ObjectToIconize != null)
            {
                if (_pictures == null) _pictures = new Dictionary<byte[], string>();
                _pictures.Clear();
                EditorApplication.update += TakeSinglePictureCoroutine;
                Capture(ObjectToIconize);
            }
            else
            {
                CaptureAndSave();
            }
        }

        private void TakeSinglePictureCoroutine()
        {
            if (_pictures.Count > 0)
            {
                UnhideOtherSceneObjects();
                foreach (var picture in _pictures)
                {
                    SaveFile(picture.Key, picture.Value);
                }
                EditorApplication.update -= TakeSinglePictureCoroutine;
            }
        }

        public void TakeMultiplePictures()
        {
            _currentPicture = 0;
            _lastPicture = -1;
            if (_pictures == null) _pictures = new Dictionary<byte[], string>();
            _pictures.Clear();
            EditorApplication.update += TakeMultiplePicturesCoroutine;
        }

        public void TakeMultiplePictures(GameObject[] objectsToTakePicturesOf)
        {
            ObjectsToIconize = objectsToTakePicturesOf;
            TakeMultiplePictures();
        }

        private void TakeMultiplePicturesCoroutine()
        {
            if (_currentPicture >= ObjectsToIconize.Length)
            {
                UnhideOtherSceneObjects();
                foreach (var picture in _pictures)
                {
                    SaveFile(picture.Key, picture.Value);
                }
                _currentPicture = 0;
                EditorApplication.update -= TakeMultiplePicturesCoroutine;
            }
            else if (_currentPicture != _lastPicture)
            {
                UnhideOtherSceneObjects();
                Capture(ObjectsToIconize[_currentPicture]);
                _lastPicture = _currentPicture;
            }
        }
    }

    public static class UnityEngineExtentions
    {
        public static Bounds GetBounds(this GameObject gameObject)
        {
            // Create a new bounds which uses the position of the root game object as a zero
            var center = gameObject.transform.position;
            var bounds = new Bounds(center, Vector3.zero);

            // For each renderer in the object, grow the bounds to include the bounds of that renderer
            foreach (var r in gameObject.GetComponentsInChildren<Renderer>())
            {
                MeshFilter filter = r.GetComponent<MeshFilter>();

                if (!r.enabled || !r.gameObject.activeInHierarchy || (filter != null && filter.sharedMesh == null)) continue;

                bounds.Encapsulate(r.bounds);
            }

            // Return the bounds
            return bounds;
        }

        public static int GetHighestComponentIndex(this Vector3 vector)
        {
            float max = float.MinValue;
            int axis = 1;
            for (int i = 0; i < 3; i++)
            {
                if (Mathf.Abs(vector[i]) > max)
                {
                    axis = i;
                    max = Mathf.Abs(vector[i]);
                }
            }
            return axis;
        }

        public static Vector3 Abs(this Vector3 vector)
        {
            Vector3 abs = vector;
            for (int i = 0; i < 3; i++)
            {
                abs[i] = Mathf.Abs(abs[i]);
            }
            return abs;
        }
    }
}
#endif