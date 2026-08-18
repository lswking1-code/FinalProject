using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 敌人软间隔与战斗站位槽：静态登记邻居，提供地面 X / 飞行 XY 排斥修正，
/// 以及同侧、同层的稳定槽位。不改状态机，由移动写入后再叠加。
/// </summary>
public static class EnemySeparation
{
    const float SameColumnEpsilon = 0.01f;
    const float FlyingYCorrectionScale = 0.4f;

    static readonly List<Enemy> live = new(32);
    static readonly List<Enemy> slotScratch = new(16);

    static readonly System.Comparison<Enemy> ByInstanceId =
        (a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID());

    public static void Register(Enemy enemy)
    {
        Prune();
        if (enemy == null || live.Contains(enemy))
            return;
        live.Add(enemy);
    }

    public static void Unregister(Enemy enemy)
    {
        if (enemy == null)
            return;
        live.Remove(enemy);
    }

    static void Prune()
    {
        for (int i = live.Count - 1; i >= 0; i--)
        {
            if (live[i] == null)
                live.RemoveAt(i);
        }
    }

    public static bool UsesFlyingLane(Enemy enemy) => enemy is FlyingEnemy;

    public static bool IsOccupying(Enemy enemy)
    {
        return enemy != null
            && enemy.isActiveAndEnabled
            && !enemy.isDead
            && enemy.IsHittable;
    }

    /// <summary>
    /// 相对玩家的左右侧。接近重叠时用朝向，避免突然换边。
    /// </summary>
    public static int GetCombatSide(Enemy self)
    {
        if (self == null)
            return 1;

        self.EnsurePlayerReference();
        if (self.player == null)
            return self.faceDir.x >= 0f ? 1 : -1;

        float dx = self.transform.position.x - self.player.position.x;
        if (Mathf.Abs(dx) < 0.05f)
            return self.faceDir.x >= 0f ? 1 : -1;

        return dx > 0f ? 1 : -1;
    }

    /// <summary>
    /// 同侧、同层存活敌人按 InstanceID 排序后的槽位下标（稳定，不随位置抖动）。
    /// </summary>
    public static int GetSlotIndex(Enemy self)
    {
        if (self == null)
            return 0;

        int side = GetCombatSide(self);
        bool flying = UsesFlyingLane(self);
        Prune();
        slotScratch.Clear();

        for (int i = 0; i < live.Count; i++)
        {
            Enemy other = live[i];
            if (!IsOccupying(other))
                continue;
            if (UsesFlyingLane(other) != flying)
                continue;
            if (GetCombatSide(other) != side)
                continue;
            if (!InSlotGroup(self, other))
                continue;
            slotScratch.Add(other);
        }

        if (slotScratch.Count == 0)
            return 0;

        slotScratch.Sort(ByInstanceId);
        int index = slotScratch.IndexOf(self);
        return index < 0 ? 0 : index;
    }

    static bool InSlotGroup(Enemy self, Enemy other)
    {
        if (self == other)
            return true;

        float radius = Mathf.Max(1f, self.combatSlotGroupRadius);
        Vector3 a = self.transform.position;
        Vector3 b = other.transform.position;
        if (UsesFlyingLane(self))
            return Vector2.Distance(a, b) <= radius;

        return Mathf.Abs(a.x - b.x) <= radius && Mathf.Abs(a.y - b.y) <= 1.5f;
    }

    public static float GetSlottedRange(Enemy self, float baseRange)
    {
        if (self == null)
            return Mathf.Max(0f, baseRange);

        int slot = GetSlotIndex(self);
        float spacing = Mathf.Max(0f, self.combatSlotSpacing);
        return Mathf.Max(0f, baseRange) + slot * spacing;
    }

    public static Vector2 GetFlyingSlotOffset(FlyingEnemy self)
    {
        if (self == null)
            return Vector2.zero;

        int slot = GetSlotIndex(self);
        int side = GetCombatSide(self);
        float xSpacing = Mathf.Max(0f, self.combatSlotSpacing);
        float ySpacing = Mathf.Max(0f, self.flyingSlotYSpacing);
        float x = side * slot * xSpacing;
        float y = (slot % 3 - 1) * ySpacing;
        return new Vector2(x, y);
    }

    public static float ComputeGroundCorrectionX(Enemy self)
    {
        if (self == null)
            return 0f;

        Prune();
        float radius = Mathf.Max(0.01f, self.separationRadius);
        float strength = Mathf.Max(0f, self.separationStrength);
        float sum = 0f;
        Vector3 pos = self.transform.position;

        for (int i = 0; i < live.Count; i++)
        {
            Enemy other = live[i];
            if (other == self || !IsOccupying(other) || UsesFlyingLane(other))
                continue;
            if (Mathf.Abs(other.transform.position.y - pos.y) > radius)
                continue;

            float dx = pos.x - other.transform.position.x;
            float absDx = Mathf.Abs(dx);
            if (absDx >= radius)
                continue;

            float dir;
            if (absDx <= SameColumnEpsilon)
                dir = self.GetInstanceID() >= other.GetInstanceID() ? 1f : -1f;
            else
                dir = Mathf.Sign(dx);

            float t = 1f - absDx / radius;
            sum += dir * strength * t * t;
        }

        return sum;
    }

    public static Vector2 ComputeFlyingCorrection(FlyingEnemy self)
    {
        if (self == null)
            return Vector2.zero;

        Prune();
        float radius = Mathf.Max(0.01f, self.separationRadius);
        float strength = Mathf.Max(0f, self.separationStrength);
        Vector2 sum = Vector2.zero;
        Vector2 pos = self.transform.position;

        for (int i = 0; i < live.Count; i++)
        {
            Enemy other = live[i];
            if (other == self || !IsOccupying(other) || !UsesFlyingLane(other))
                continue;

            Vector2 delta = pos - (Vector2)other.transform.position;
            float dist = delta.magnitude;
            if (dist >= radius)
                continue;

            Vector2 dir;
            if (dist <= SameColumnEpsilon)
            {
                int sign = self.GetInstanceID() >= other.GetInstanceID() ? 1 : -1;
                dir = new Vector2(sign, 0.35f * sign);
            }
            else
            {
                dir = delta / dist;
            }

            float t = 1f - dist / radius;
            sum += dir * strength * t * t;
        }

        sum.y *= FlyingYCorrectionScale;
        return sum;
    }
}
