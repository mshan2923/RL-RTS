using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(InterfaceReference<>), true)]
public class InterfaceReferencePropertyDrawer : PropertyDrawer
{
    SerializedProperty property_Target;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        property_Target = property.FindPropertyRelative("m_Target");

        var interfaceType = GetInterfaceType();
        Rect rect = new(position.x, position.y, position.width, EditorGUI.GetPropertyHeight(property_Target));

        EditorGUI.BeginChangeCheck();
        var newObj = EditorGUI.ObjectField(rect, label, property_Target.objectReferenceValue, typeof(UnityEngine.Object), true);

        if (EditorGUI.EndChangeCheck())
        {

            if (EditorGUI.EndChangeCheck())
            {
                Debug.Log($"CHANGED! newObj: {newObj}, null? {newObj == null}");
            }

            if (newObj == null)
            {
                property_Target.objectReferenceValue = null;
                Debug.Log("null");
            }
            else if (interfaceType != null && interfaceType.IsInstanceOfType(newObj))
            {
                property_Target.objectReferenceValue = newObj;
                Debug.Log("Set");
            }
            else if (newObj is GameObject go)
            {
                var comp = go.GetComponents<Component>().FirstOrDefault(c => interfaceType.IsInstanceOfType(c));
                property_Target.objectReferenceValue = comp;
                if (comp == null)
                    Debug.LogWarning($"{go.name}에 {interfaceType.Name}을(를) 구현하는 컴포넌트가 없음");
            }
            else
            {
                Debug.LogWarning($"{newObj.name}은(는) {interfaceType?.Name}을(를) 구현하지 않음");
            }
        }
        EditorGUI.EndProperty();
        property.serializedObject.ApplyModifiedProperties();
    }
    

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return base.GetPropertyHeight(property, label);
    }

    System.Type GetInterfaceType()
    {
        System.Type type = fieldInfo.FieldType;
        System.Type[] typeArguments = type.GenericTypeArguments;
        if (typeof(IEnumerable).IsAssignableFrom(type))
        {
            typeArguments = fieldInfo.FieldType.GenericTypeArguments[0].GenericTypeArguments;
        }
        if (typeArguments == null || typeArguments.Length == 0)
            return null;

        return typeArguments[0];
    }
}