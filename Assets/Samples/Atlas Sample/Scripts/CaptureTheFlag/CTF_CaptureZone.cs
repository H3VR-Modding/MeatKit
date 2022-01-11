using UnityEngine;

namespace nrgill28.AtlasSampleScene
{
    public class CTF_CaptureZone : MonoBehaviour
    {
        public CTF_Manager Manager;
        public CTF_Team Team;

        public void OnTriggerEnter(Collider other)
        {
            // If the object is the other team's flag, capture it!
            var flag = other.GetComponent<CTF_Flag>();
            if (flag && flag.Team != Team)
            {
#if H3VR_IMPORTED
                Manager.FlagCaptured(flag);
#endif
            }
        }
    }
}