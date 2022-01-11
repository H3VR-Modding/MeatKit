#if H3VR_IMPORTED
using FistVR;
using UnityEngine;

namespace nrgill28.AtlasSampleScene
{
    public class CTF_Sosig : MonoBehaviour
    {
        public CTF_Team Team;
        public CTF_Flag HeldFlag;
        public Sosig Sosig;

        private void Awake()
        {
            Sosig = GetComponent<Sosig>();
        }

        private void Update()
        {
            if (HeldFlag)
            {
                // Move the flag into position on the Sosig
                var pos = Sosig.transform.position - Sosig.transform.forward * 0.1f;
                HeldFlag.transform.SetPositionAndRotation(pos, Sosig.transform.rotation);
            }
        }
    }
}
#endif
