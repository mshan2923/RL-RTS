using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public interface UnitEnumInterface
{
    public void Invoke(UnitEnum unitEnum, NativeArray<Entity> unitArray);
    public void EndInvoke(UnitEnum unitEnum);
}

[System.Serializable]
public abstract class UnitEnumAbs : UnitEnumInterface
{
    private static UnitEnumAbs _instance;
    public static UnitEnumAbs instance {get => _instance;}
    public static UnitEnumAbs initilize<T>() where T : UnitEnumAbs, new()
    {
        if (_instance == null)
            _instance = new T();

        return _instance;
    }
    public abstract void Invoke(UnitEnum unitEnum, NativeArray<Entity> unitArray);
    public abstract void EndInvoke(UnitEnum unitEnum);
}