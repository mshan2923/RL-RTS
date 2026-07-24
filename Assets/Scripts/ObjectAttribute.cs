
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

// 1. 커스텀 어트리뷰트 정의
public class ObjectFieldAttribute : PropertyAttribute
{
    public System.Type InterfaceType { get; private set; }
    public bool AllowScene;

    public ObjectFieldAttribute(System.Type interfaceType)
    {
        InterfaceType = interfaceType;
    }
}

#if UNITY_EDITOR
// 2. 어트리뷰트를 처리할 PropertyDrawer 구현
[CustomPropertyDrawer(typeof(ObjectFieldAttribute))]
public class ObjectAttributeDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        ObjectFieldAttribute attr = (ObjectFieldAttribute)attribute;

        System.Type interfaceType = attr.InterfaceType;

        EditorGUI.BeginProperty(position, label, property);

        Object obj = EditorGUI.ObjectField(position, label, property.objectReferenceValue, interfaceType, attr.AllowScene);

        // if (obj != null)
        // {
        //     System.Type interfaceType = attr.InterfaceType;
        //     bool assigned = false;

        //     // 게임 오브젝트를 드래그 앤 드롭한 경우 컴포넌트 탐색
        //     if (obj is GameObject go)
        //     {
        //         Component comp = go.GetComponent(interfaceType) as Component;
        //         if (comp != null)
        //         {
        //             property.objectReferenceValue = comp;
        //             assigned = true;
        //         }
        //     }
        //     // 컴포넌트나 스크립터블 오브젝트인 경우
        //     else if (interfaceType.IsAssignableFrom(obj.GetType()))
        //     {
        //         property.objectReferenceValue = obj;
        //         assigned = true;
        //     }

        //     if (!assigned)
        //     {
        //         Debug.LogWarning($"{obj.name}은(는) {interfaceType.Name} 인터페이스를 구현하지 않았어.");
        //         property.objectReferenceValue = null;
        //     }
        // }
        // else
        // {
        //     property.objectReferenceValue = null;
        // }

        EditorGUI.EndProperty();
    }
}
#endif