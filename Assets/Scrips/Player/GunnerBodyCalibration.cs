using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Offsets are in the character root's local XY space, never texture UVs.</summary>
[CreateAssetMenu(menuName = "Gunner/Body Calibration")]
public sealed class GunnerBodyCalibration : ScriptableObject
{
    public enum MuzzleDirection { Forward, Crouch, Up, Down }
    [Serializable]
    public struct MuzzleEntry
    {
        public Sprite sprite;
        public int weaponId;
        public MuzzleDirection direction;
        public Vector2 pixel;
    }
    [SerializeField] List<MuzzleEntry> muzzles = new List<MuzzleEntry>();

    public bool TryGetMuzzle(Sprite sprite, int weaponId, MuzzleDirection direction, out Vector2 pixel)
    {
        pixel = Vector2.zero;
        if (sprite == null) return false;
        foreach (var entry in muzzles)
            if (entry.sprite == sprite && entry.weaponId == weaponId && entry.direction == direction)
            { pixel = entry.pixel; return true; }
        return false;
    }

    public void SetMuzzle(Sprite sprite, int weaponId, MuzzleDirection direction, Vector2 pixel)
    {
        if (sprite == null) return;
        if (float.IsNaN(pixel.x) || float.IsNaN(pixel.y) || float.IsInfinity(pixel.x) || float.IsInfinity(pixel.y))
            throw new ArgumentException("Muzzle pixel must be finite.");
        RemoveMuzzle(sprite, weaponId, direction);
        muzzles.Add(new MuzzleEntry { sprite = sprite, weaponId = weaponId, direction = direction, pixel = pixel });
    }

    public void RemoveMuzzle(Sprite sprite, int weaponId, MuzzleDirection direction) =>
        muzzles.RemoveAll(e => e.sprite == sprite && e.weaponId == weaponId && e.direction == direction);

    public static Vector2 MuzzleLocalPosition(Sprite sprite, Vector2 pixel, bool flipX, bool flipY)
    {
        var local = (pixel - sprite.pivot) / sprite.pixelsPerUnit;
        return Vector2.Scale(local, new Vector2(flipX ? -1 : 1, flipY ? -1 : 1));
    }
    [Serializable]
    public struct Entry
    {
        public Sprite sprite;
        public Vector2 offset;
        public Entry(Sprite sprite, Vector2 offset) { this.sprite = sprite; this.offset = offset; }
    }

    [SerializeField] Sprite referenceUpper;
    [SerializeField] Sprite referenceLower;
    [SerializeField] List<Entry> upperOffsets = new List<Entry>();
    [SerializeField] List<Entry> lowerOffsets = new List<Entry>();
    Dictionary<Sprite, Vector2> upperCache, lowerCache;

    public Sprite ReferenceUpper => referenceUpper;
    public Sprite ReferenceLower => referenceLower;
    public bool HasEntries => upperOffsets.Count != 0 || lowerOffsets.Count != 0;
    public IReadOnlyList<Entry> UpperEntries => upperOffsets;
    public IReadOnlyList<Entry> LowerEntries => lowerOffsets;

    public void SetReferences(Sprite upper, Sprite lower)
    {
        if (HasEntries && (upper != referenceUpper || lower != referenceLower))
            throw new InvalidOperationException("Clear calibration before changing reference sprites.");
        referenceUpper = upper;
        referenceLower = lower;
        Invalidate();
    }

    public bool TryGetOffset(bool upper, Sprite sprite, out Vector2 offset)
    {
        offset = Vector2.zero;
        if (sprite == null) return false;
        // Fix the gauge: the reference lower frame has zero waist displacement.
        if (!upper && sprite == referenceLower) return true;
        if (upperCache == null || lowerCache == null)
        {
            upperCache = BuildCache(upperOffsets);
            lowerCache = BuildCache(lowerOffsets);
        }
        return (upper ? upperCache : lowerCache).TryGetValue(sprite, out offset);
    }

    public Vector2 GetOffset(bool upper, Sprite sprite)
    {
        TryGetOffset(upper, sprite, out var offset);
        return offset;
    }

    public Vector2 GetCombinedOffset(Sprite upper, Sprite lower)
    {
        return upper == null || lower == null ? Vector2.zero : GetOffset(true, upper) + GetOffset(false, lower);
    }

    public void SetOffset(bool upper, Sprite sprite, Vector2 offset)
    {
        if (sprite == null) return;
        if (float.IsNaN(offset.x) || float.IsNaN(offset.y) || float.IsInfinity(offset.x) || float.IsInfinity(offset.y))
            throw new ArgumentException("Offset must be finite.");
        if (!upper && sprite == referenceLower) offset = Vector2.zero;
        var entries = upper ? upperOffsets : lowerOffsets;
        int index = entries.FindIndex(e => e.sprite == sprite);
        var entry = new Entry(sprite, offset);
        if (index < 0) entries.Add(entry); else entries[index] = entry;
        Invalidate();
    }

    public void RemoveOffset(bool upper, Sprite sprite)
    {
        (upper ? upperOffsets : lowerOffsets).RemoveAll(e => e.sprite == sprite);
        Invalidate();
    }

    public void ClearOffsets()
    {
        upperOffsets.Clear();
        lowerOffsets.Clear();
        Invalidate();
    }

    public void CopyFrom(GunnerBodyCalibration other)
    {
        referenceUpper = other.referenceUpper;
        referenceLower = other.referenceLower;
        upperOffsets = new List<Entry>(other.upperOffsets);
        lowerOffsets = new List<Entry>(other.lowerOffsets);
        muzzles = new List<MuzzleEntry>(other.muzzles);
        Invalidate();
    }

    public void Invalidate() { upperCache = null; lowerCache = null; }
    void OnEnable() => Invalidate();
    void OnValidate() => Invalidate();

    static Dictionary<Sprite, Vector2> BuildCache(List<Entry> entries)
    {
        var result = new Dictionary<Sprite, Vector2>();
        foreach (var entry in entries)
            if (entry.sprite != null) result[entry.sprite] = entry.offset;
        return result;
    }
}
