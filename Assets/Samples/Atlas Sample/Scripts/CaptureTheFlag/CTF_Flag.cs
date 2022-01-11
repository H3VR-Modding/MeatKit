using UnityEngine;
#if H3VR_IMPORTED
using FistVR;
using UnityEngine.AI;
#endif

namespace nrgill28.AtlasSampleScene
{
    public class CTF_Flag :
#if H3VR_IMPORTED
        FVRPhysicalObject
#else
    MonoBehaviour
#endif
    {
        // Configuration variables
        [Header("Flag stuffs")]
        public CTF_Team Team;

        public float RespawnDelay = 10f;
        public Vector3 FloorOffset = new Vector3(0f, 0.25f, 0f);

        // Where to return the flag to
        private Vector3 _resetPosition;
        private Quaternion _resetRotation;

        // Flag state
        private Transform _followTransform;
        private bool _isHeld;
        private bool _isTaken;
        private float _timer;

#if H3VR_IMPORTED
        private CTF_Sosig _heldBy;
        public override void Awake()
        {
            base.Awake();
            _resetPosition = transform.position;
            _resetRotation = transform.rotation;
        }
#endif

        private void Update()
        {
            // If the flag is on the ground, tick down the timer, and if it expires, return the flag.
            if (_isTaken && !_isHeld)
            {
                _timer -= Time.deltaTime;
                if (_timer < 0) ReturnFlag();
            }
        }

        // Called when the flag is taken by the player / a sosig
        public void Take()
        {
            _isHeld = true;
            _isTaken = true;
        }

        // Called when the flag is dropped by the player / a sosig
        public void Drop()
        {
#if H3VR_IMPORTED
            // Mark the flag no longer held and reset the timer
            IsHeld = false;
            _timer = RespawnDelay;

            // Try to place this back in a valid position where it can be grabbed
            NavMeshHit hit;
            NavMesh.SamplePosition(transform.position, out hit, 100, -1);
            transform.position = hit.position + FloorOffset;
            transform.rotation = Quaternion.identity;
#endif
        }

        // Called when the flag should return to it's starting position when it is captured / left on the ground for too long
        public void ReturnFlag()
        {
#if H3VR_IMPORTED
            // If the player is holding the flag force them to drop it
            if (IsHeld) ForceBreakInteraction();

            // Or if it's held by a Sosig, take if from them
            if (_heldBy) _heldBy.HeldFlag = null;

            // Reset the position and rotation
            transform.SetPositionAndRotation(_resetPosition, _resetRotation);
            _isTaken = false;
#endif
        }

        private void OnTriggerEnter(Collider other)
        {
#if H3VR_IMPORTED
            // If the flag isn't dropped we don't care
            if (_isHeld) return;

            // Check if a sosig is going to pick this flag up
            var sosig = other.GetComponentInParent<CTF_Sosig>();
            if (sosig)
            {
                // The Sosig can't do anything if it's not in control
                if (sosig.Sosig.BodyState != Sosig.SosigBodyState.InControl) return;

                // If this sosig is on our team, return it
                if (sosig.Team == Team) ReturnFlag();

                // Otherwise the Sosig is going to pick it up
                else
                {
                    _heldBy = sosig;
                    sosig.HeldFlag = this;
                    Take();
                }
            }
#endif
        }

#if H3VR_IMPORTED
        public override void BeginInteraction(FVRViveHand hand)
        {
            base.BeginInteraction(hand);
            Take();
        }

        public override void EndInteraction(FVRViveHand hand)
        {
            base.EndInteraction(hand);
            Drop();
        }
#endif
    }
}