using System;
using System.Collections;
using UnityEngine;
#if H3VR_IMPORTED
using FistVR;
#endif

public class PopupTarget : MonoBehaviour
#if H3VR_IMPORTED
    , IFVRDamageable
#endif
{
    [Flags]
    public enum TargetRange
    {
        Near = 1,
        Mid = 2,
        Far = 4,

        All = Near | Mid | Far
    }

    public PopupTargetManager Manager;
    public TargetRange Range;

    // The transform that we will translate / rotate when setting and resetting the target
    public Transform Pivot;

    // The local rotation of the pivot when it is set
    public Vector3 SetRotation;
    private Quaternion _startRotation;
    private Quaternion _endRotation;
    private bool _set;

    public bool Set
    {
        get { return _set; }
        set
        {
            if (_set == value) return;
            _set = value;
            StartCoroutine(_set ? RotateTo(_startRotation, _endRotation) : RotateTo(_endRotation, _startRotation));
        }
    }

    private void Awake()
    {
        _startRotation = Pivot.rotation;
        _endRotation = Quaternion.Euler(SetRotation + _startRotation.eulerAngles);
    }

#if H3VR_IMPORTED
    // Event for when we are damaged by something in the game
    void IFVRDamageable.Damage(Damage dam)
    {
        // If we're not set or damage was non-projectile, ignore.
        if (!Set || dam.Class != Damage.DamageClass.Projectile) return;

        // We were hit so unset the target
        Set = false;
        Manager.TargetHit(this);
    }
#endif

    private IEnumerator RotateTo(Quaternion from, Quaternion to)
    {
        const float duration = 0.25f;
        var elapsed = 0f;

        while (elapsed < duration)
        {
            yield return null;
            elapsed += Time.deltaTime;
            Pivot.localRotation = Quaternion.Slerp(from, to, elapsed / duration);
        }

        // Set this at the end so there's no slight errors
        Pivot.rotation = to;
    }
}
