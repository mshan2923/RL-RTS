using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities.UniversalDelegates;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[System.Serializable]
public class UnitEnumDBStruct
{
    public UnitEnum type;
    
    // 제네릭 대신 UnityEngine.Object 기반으로 선언
    [SerializeReference]
    public List<UnitEnumInterface> Pure;
}

[CreateAssetMenu(fileName = "UnitEnumDataTable", menuName = "Scriptable Objects/UnitEnumDB")]
public class UnitEnumDB : ScriptableObject
{
    public List<UnitEnumDBStruct> Types;
}



[CustomPropertyDrawer(typeof(UnitEnumInterface), true)]
public class UnitEnumInterfaceDrawer : PropertyDrawer
{
    // 인터페이스를 구현하는 콘크리트 타입 캐싱
    static Type[] _types;
    static string[] _typeNames;

    static void CacheTypes()
    {
        if (_types != null) return;

        _types = TypeCache.GetTypesDerivedFrom<UnitEnumInterface>()
            .Where(t => !t.IsAbstract && !t.IsInterface && !t.IsGenericTypeDefinition)
            .ToArray();

        _typeNames = _types.Select(t => t.Name).ToArray();
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        CacheTypes();
        EditorGUI.BeginProperty(position, label, property);

        // 현재 할당된 타입 이름 (없으면 "None")
        string currentTypeName = GetShortTypeName(property.managedReferenceFullTypename);
        int currentIndex = Array.IndexOf(_typeNames, currentTypeName);

        Rect popupRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

        EditorGUI.BeginChangeCheck();
        int newIndex = EditorGUI.Popup(popupRect, label.text,
            currentIndex, _typeNames.Length > 0 ? _typeNames : new[] { "No Types Found" });

        if (EditorGUI.EndChangeCheck() && newIndex >= 0 && newIndex < _types.Length)
        {
            // 콘크리트 인스턴스를 새로 생성해 넣어줌
            object instance = Activator.CreateInstance(_types[newIndex]);
            property.managedReferenceValue = instance;
        }

        // 타입이 정해졌으면 그 아래에 실제 필드들 그리기
        if (currentIndex >= 0)
        {
            EditorGUI.indentLevel++;
            Rect fieldRect = new Rect(position.x, popupRect.yMax + EditorGUIUtility.standardVerticalSpacing,
                position.width, EditorGUI.GetPropertyHeight(property, true) - popupRect.height);
            EditorGUI.PropertyField(fieldRect, property, GUIContent.none, true);
            EditorGUI.indentLevel--;
        }


        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        CacheTypes();
        string currentTypeName = GetShortTypeName(property.managedReferenceFullTypename);
        bool hasType = Array.IndexOf(_typeNames, currentTypeName) >= 0;

        float height = EditorGUIUtility.singleLineHeight;
        if (hasType)
            height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(property, true);

        return height;
    }

    static string GetShortTypeName(string fullTypename)
    {
        // managedReferenceFullTypename 포맷: "AssemblyName TypeFullName"
        if (string.IsNullOrEmpty(fullTypename)) return null;
        int idx = fullTypename.LastIndexOf(' ');
        if (idx < 0) return fullTypename;
        string full = fullTypename.Substring(idx + 1);
        int dotIdx = full.LastIndexOf('.');
        return dotIdx >= 0 ? full.Substring(dotIdx + 1) : full;
    }
}