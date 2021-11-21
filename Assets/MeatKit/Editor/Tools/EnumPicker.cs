// Adapted from https://gist.github.com/kalineh/ad5135946f2009c36f755eea0a880998

using System;
using System.Collections.Generic;
using System.Linq;
#if H3VR_IMPORTED
using FistVR;
#endif
using UnityEditor;
using UnityEngine;

public class EnumPickerWindow : EditorWindow
{
    private static GUIStyle _regularStyle;
    private static GUIStyle _selectedStyle;

    private string _enumName;
    private string _filter;

    private Action<string> _onSelectCallback;

    private EditorWindow _parent;
    private Vector2 _scroll;
    private List<string> _valuesFiltered;
    private List<string> _valuesRaw;

    private void OnGUI()
    {
        GUILayout.Label(string.Format("Enum Type: {0}", _enumName));

        GUI.SetNextControlName("filter");
        var filterUpdate = GUILayout.TextField(_filter);
        if (filterUpdate != _filter)
            FilterValues(filterUpdate);

        // always focused
        GUI.FocusControl("filter");

        _scroll = GUILayout.BeginScrollView(_scroll);

        for (var i = 0; i < _valuesFiltered.Count; ++i)
        {
            var value = _valuesFiltered[i];
            var style = i == 0 ? _selectedStyle : _regularStyle;
            var rect = GUILayoutUtility.GetRect(new GUIContent(value), style);

            var clicked = GUI.Button(rect, value);
            if (clicked)
            {
                GUILayout.EndScrollView();

                _onSelectCallback(value);
                Close();
                _parent.Repaint();
                _parent.Focus();

                return;
            }
        }

        GUILayout.EndScrollView();

        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
        {
            if (_valuesFiltered.Count > 0)
                _onSelectCallback(_valuesFiltered[0]);

            Close();
            _parent.Repaint();
            _parent.Focus();
        }

        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            Close();
            _parent.Repaint();
            _parent.Focus();
        }
    }

    public void OnLostFocus()
    {
        Close();
    }

    public void ShowCustom(string enumName, List<string> values, Rect rect, Action<string> onSelect)
    {
        _regularStyle = new GUIStyle(EditorStyles.label);
        _regularStyle.active = _regularStyle.normal;

        _selectedStyle = new GUIStyle(EditorStyles.label);
        _selectedStyle.normal = _selectedStyle.focused;
        _selectedStyle.active = _selectedStyle.focused;

        _enumName = enumName;
        _valuesRaw = new List<string>(values);
        _valuesFiltered = new List<string>(values);
        _filter = "";
        _onSelectCallback = onSelect;

        _parent = focusedWindow;

        var screenRect = rect;
        var screenSize = new Vector2(400, 400);

        screenRect.position = GUIUtility.GUIToScreenPoint(screenRect.position);

        ShowAsDropDown(screenRect, screenSize);
        Focus();

        GUI.FocusControl("filter");
    }

    private void FilterValues(string filterUpdate)
    {
        _filter = filterUpdate;
        var filterLower = _filter.ToLower();
        _valuesFiltered.Clear();
        foreach (var value in from value in _valuesRaw
            let lower = value.ToLower()
            where lower.Contains(filterLower)
            select value)
            _valuesFiltered.Add(value);
    }
}

public class EnumPicker : PropertyDrawer
{
    private EnumPickerWindow _window;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        Array valuesRaw = null;
        Type enumType = fieldInfo.FieldType;
        if (enumType.IsArray) enumType = enumType.GetElementType(); //this turns the enum array into just the enum to prevent issues with arrays of enums
        
        valuesRaw = Enum.GetValues(enumType);
        if (valuesRaw.Length <= 0)
            return;
        var valuesStr = new List<string>();
        for (var i = 0; i < valuesRaw.Length; ++i)
        {
            object raw = valuesRaw.GetValue(i);
            var str = raw.ToString();
            valuesStr.Add(str);
        }
        string enumName = enumType.Name;
        string currentName = Enum.GetName(enumType, property.intValue);
        EditorGUI.PrefixLabel(position, label);
        GUI.SetNextControlName(property.propertyPath);
        var fieldRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y,
            position.width - EditorGUIUtility.labelWidth, position.height);
        if (GUI.Button(fieldRect, currentName, EditorStyles.popup))
        {
            _window = EditorWindow.GetWindow<EnumPickerWindow>();
            Action<string> callback = str =>
            {
                var index = (int) Convert.ChangeType(Enum.Parse(enumType, str), enumType);
                property.serializedObject.Update();
                property.intValue = index;
                property.serializedObject.ApplyModifiedProperties();
            };
            _window.ShowCustom(enumName, valuesStr, fieldRect, callback);
            _window.Focus();
        }
    }
}

#if H3VR_IMPORTED
[CustomPropertyDrawer(typeof(FireArmMagazineType))]
[CustomPropertyDrawer(typeof(FireArmClipType))]
[CustomPropertyDrawer(typeof(FireArmRoundClass))]
[CustomPropertyDrawer(typeof(ItemSpawnerObjectDefinition.ItemSpawnerCategory))]
[CustomPropertyDrawer(typeof(ItemSpawnerID.EItemCategory))]
[CustomPropertyDrawer(typeof(ItemSpawnerID.ESubCategory))]
[CustomPropertyDrawer(typeof(FireArmRoundType))]
[CustomPropertyDrawer(typeof(SosigEnemyID))]
public class EnumDrawers : EnumPicker
{
}
#endif
