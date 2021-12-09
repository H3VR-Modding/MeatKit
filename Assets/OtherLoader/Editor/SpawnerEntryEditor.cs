using FistVR;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;


[CustomEditor(typeof(OtherLoader.ItemSpawnerEntry), true)]
public class SpawnerEntryEditor : Editor
{
    public bool hasInit = false;
    public bool isItemIDEmpty = false;

    public override void OnInspectorGUI()
    {
        serializedObject.ApplyModifiedProperties();

        if (!hasInit)
        {
            isItemIDEmpty = string.IsNullOrEmpty(serializedObject.FindProperty("MainObjectID").stringValue);
            hasInit = true;
        }

        var property = serializedObject.GetIterator();
        if (!property.NextVisible(true)) return;

        do {

            if(property.name == "EntryPath")
            {
                List<string> values = property.stringValue.Split('/').ToList();
                property.stringValue = ((ItemSpawnerV2.PageMode)serializedObject.FindProperty("Page").enumValueIndex).ToString();
                property.stringValue += "/";

                if(((ItemSpawnerID.ESubCategory)serializedObject.FindProperty("SubCategory").enumValueIndex) != ItemSpawnerID.ESubCategory.None)
                {
                    property.stringValue += ((ItemSpawnerID.ESubCategory)serializedObject.FindProperty("SubCategory").enumValueIndex).ToString();
                }
                else
                {
                    property.stringValue += values[1];
                }

                //Finally, add the end of the path based on the objectID
                string itemID = serializedObject.FindProperty("MainObjectID").stringValue;
                if (!string.IsNullOrEmpty(itemID))
                {
                    //If the itemID field is currently filled, but previously wasn't, we fill maintain all of the path and then add the itemID
                    if (isItemIDEmpty)
                    {
                        for (int i = 2; i < values.Count; i++)
                        {
                            property.stringValue += "/" + values[i];
                        }

                        isItemIDEmpty = false;
                    }


                    //If the itemID field was already filled previously, we can just draw everything until the itemID, and then add the itemID
                    else
                    {
                        for (int i = 2; i < values.Count - 1; i++)
                        {
                            property.stringValue += "/" + values[i];
                        }
                    }

                    property.stringValue += "/" + serializedObject.FindProperty("MainObjectID").stringValue;
                }

                else
                {
                    isItemIDEmpty = true;

                    for (int i = 2; i < values.Count; i++)
                    {
                        property.stringValue += "/" + values[i];
                    }
                }
            }

            DrawProperty(property);
        }
        while (property.NextVisible(false));

    }

    protected virtual void DrawProperty(SerializedProperty property)
    {
        EditorGUILayout.PropertyField(property, true);
    }
}
