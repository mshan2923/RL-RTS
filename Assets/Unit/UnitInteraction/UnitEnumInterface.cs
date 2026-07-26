using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public struct UnitActionContext : IDisposable
{
    public UnitEnum unitEnum;
    public NativeArray<Entity> Entities;

    public void Dispose()
    {
        if (Entities.IsCreated)
            Entities.Dispose();
    }
}
public interface UnitEnumInterface
{
    string GetLabel(UnitActionContext ctx);   // UI 표시용
    bool CanExecute(UnitActionContext ctx);   // 버튼 활성화 여부
    void Execute(UnitActionContext ctx);      // 실제 실행 (여기서 파라미터 꺼내 씀)

}

public interface Ikill
{
    void invoke(UnitEnum unitEnum);
}