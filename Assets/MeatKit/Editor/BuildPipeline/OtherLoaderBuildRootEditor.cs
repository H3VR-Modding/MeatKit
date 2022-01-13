

#if H3VR_IMPORTED

using MeatKit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

using FistVR;

[CustomEditor(typeof(OtherLoaderBuildRoot), true)]
public class OtherLoaderBuildRootEditor : BuildItemEditor
{

    private PathNode pathRoot;

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        if (ValidationMessages.Count > 0) return;

        pathRoot = new PathNode("");

        SerializedProperty firstList = serializedObject.FindProperty("BuildItemsFirst").Copy();
        PopulatePathTree(firstList);

        SerializedProperty anyList = serializedObject.FindProperty("BuildItemsAny").Copy();
        PopulatePathTree(anyList);

        SerializedProperty lastList = serializedObject.FindProperty("BuildItemsLast").Copy();
        PopulatePathTree(lastList);

        string pathString = GetPathString(pathRoot);

        EditorStyles.helpBox.richText = true;

        DrawHorizontalLine();
        EditorGUILayout.HelpBox("Category Overview", MessageType.Info);
        EditorGUILayout.HelpBox(pathString.Trim(), MessageType.None);
    }





    private void PopulatePathTree(SerializedProperty itemList)
    {


        int listCount = itemList.arraySize;
        for (int i = 0; i < listCount; i++)
        {
            //This is terrible, but it seems like it's the only way to get child properties
            //See here: https://answers.unity.com/questions/543010/odd-behavior-of-findpropertyrelative.html 
            SerializedObject entryList = new SerializedObject(itemList.GetArrayElementAtIndex(i).objectReferenceValue);

            SerializedProperty entries = entryList.FindProperty("SpawnerEntries");
            int entryCount = entries.arraySize;
            for (int j = 0; j < entryCount; j++)
            {
                SerializedObject entry = new SerializedObject(entries.GetArrayElementAtIndex(j).objectReferenceValue);

                SerializedProperty entryPath = entry.FindProperty("EntryPath");

                string path = entryPath.stringValue;
                string[] pathParts = path.Split('/');
                string currPath = "";

                PathNode currNode = pathRoot;
                for(int k = 0; k < pathParts.Length; k++)
                {
                    currPath += (k == 0?"":"/") + pathParts[k];
                    
                    PathNode nextNode = currNode.children.FirstOrDefault(o => currPath == o.path);

                    if(nextNode == null)
                    {
                        nextNode = new PathNode(currPath);
                        currNode.children.Add(nextNode);
                    }

                    if(k == 0)
                    {
                        if (Enum.IsDefined(typeof(ItemSpawnerV2.PageMode), pathParts[k]))
                        {
                            nextNode.declared = true;
                        }
                    }

                    if(k == 1)
                    {
                        if (Enum.IsDefined(typeof(ItemSpawnerID.ESubCategory), pathParts[k]))
                        {
                            nextNode.declared = true;
                        }
                    }

                    currNode = nextNode;
                }

                currNode.declared = true;
            }
        }
    }




    private string GetPathString(PathNode node, int currDepth = -1)
    {
        string pathString = "";
        if(currDepth >= 0)
        {
            string styleStart = "<b>";
            string styleEnd = "</b>";

            if(!node.declared)
            {
                styleStart = "<b><color=red>";
                styleEnd = "</color></b>";
            }

            pathString = styleStart + new string(' ', currDepth * 8) + node.path.Split('/').Last() + styleEnd + "\n";
        }
        
        foreach(PathNode child in node.children)
        {
            pathString += GetPathString(child, currDepth + 1);
        }

        return pathString;
    }


    private void DrawHorizontalLine()
    {
        EditorGUILayout.Space();
        var rect = EditorGUILayout.BeginHorizontal();
        Handles.color = Color.gray;
        Handles.DrawLine(new Vector2(rect.x - 15, rect.y), new Vector2(rect.width + 15, rect.y));
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();
    }


    private class PathNode
    {
        public string path;

        public bool declared = false;

        public List<PathNode> children = new List<PathNode>();

        public PathNode(string path)
        {
            this.path = path;
        }
    }

}

#endif
