using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public static class DOTS_Mecro
{
    public static EntityQuery UnitParmQuery(ref SystemState state)
    {
        using var build = new EntityQueryBuilder(Allocator.Temp);
        return build.WithAll<CUnitParams, UnitEnumComponent>().Build(ref state);
    }
    public static EntityQuery UnitParmQuery(EntityManager em)
    {
        using var build = new EntityQueryBuilder(Allocator.Temp);
        return build.WithAll<CUnitParams, UnitEnumComponent>().Build(em);
    }

    public static CUnitParams GetUnitParm(EntityQuery unitQuery,  UnitEnum unitType)
    {
            if (unitQuery.CalculateEntityCount() > 0)
            {
                using var parms = unitQuery.ToComponentDataArray<CUnitParams>(Allocator.Temp);
                using var unitenums = unitQuery.ToComponentDataArray<UnitEnumComponent>(Allocator.Temp);

                for(int i = 0; i < unitQuery.CalculateEntityCount(); i++)
                {
                    if (unitenums[i].type == unitType)
                    {
                        return parms[i];
                    }
                }

                if (parms.IsCreated) parms.Dispose();
                if (unitenums.IsCreated) unitenums.Dispose();
            }

            return default;
    }

        public static void GetUnitParm(EntityQuery unitQuery, ref NativeHashMap<UnitEnumComponent, CUnitParams> pairs)
    {
            if (unitQuery.CalculateEntityCount() > 0)
            {
                var parms = unitQuery.ToComponentDataArray<CUnitParams>(Allocator.TempJob);
                var unitenums = unitQuery.ToComponentDataArray<UnitEnumComponent>(Allocator.TempJob);

                for(int i = 0; i < unitQuery.CalculateEntityCount(); i++)
                {
                    pairs.Add(unitenums[i], parms[i]);
                }

                if (parms.IsCreated) parms.Dispose();
                if (unitenums.IsCreated) unitenums.Dispose();
            }

    }

}
