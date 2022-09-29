using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MagazineGizmos : MonoBehaviour {

    public float GizmoSize;

#if UNITY_EDITOR
    public void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(this.transform.position, GizmoSize);
    }
#else
    public void OnAwake()
    {
        Destroy(this);
    }
#endif
}
