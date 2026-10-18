using UnityEngine;

namespace MadeInArizona
{
    /// <summary>Small runtime mounts for accessories; tracks damage-profile activation.</summary>
    public sealed class VehicleAccessoryVisuals : MonoBehaviour
    {
        Transform bar, spikes;
        Light lamp;
        VehicleController owner;
        static readonly Color Steel=new Color(.16f,.19f,.21f);
        static readonly Color Amber=new Color(1f,.82f,.43f);

        static Transform Box(Transform parent,string name,Vector3 position,Vector3 size,Color color)
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name=name;obj.transform.SetParent(parent,false);obj.transform.localPosition=position;obj.transform.localScale=size;
            var collider=obj.GetComponent<Collider>();if(collider)Destroy(collider);
            var renderer=obj.GetComponent<Renderer>();
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard"));
            material.color=color;renderer.material=material;
            return obj.transform;
        }
        public void Refresh(VehicleController vehicle)
        {
            owner=vehicle;if(!owner||!owner.Visual||owner.Stats==null)return;
            if(!bar)
            {
                bar=new GameObject("Light Bar Accessory").transform;bar.SetParent(owner.Visual,false);
                float roof=owner.Definition.rideHeight+1.32f;
                Box(bar,"Mount",new Vector3(0,roof,0),new Vector3(owner.Definition.trackWidth*.65f,.09f,.18f),Steel);
                Box(bar,"Luminous Strip",new Vector3(0,roof+.055f,.07f),new Vector3(owner.Definition.trackWidth*.56f,.07f,.06f),Amber);
                var lightObject=new GameObject("Auxiliary beam");lightObject.transform.SetParent(bar,false);
                lightObject.transform.localPosition=new Vector3(0,roof,.18f);
                lamp=lightObject.AddComponent<Light>();lamp.type=LightType.Spot;lamp.color=Amber;lamp.range=34;
                lamp.spotAngle=68;lamp.innerSpotAngle=35;lamp.shadows=LightShadows.None;
            }
            if(!spikes)
            {
                spikes=new GameObject("Wheel Spikes Accessory").transform;spikes.SetParent(owner.Visual,false);
                float x=owner.Definition.trackWidth*.58f,z=owner.Definition.wheelbase*.43f;
                for(int i=0;i<4;i++)
                {
                    float side=i%2==0?-1:1;
                    float fore=i<2?1:-1;
                    Box(spikes,"Spike guard",new Vector3(side*x,.48f,fore*z),new Vector3(.26f,.17f,.46f),Steel);
                    Box(spikes,"Spike tip",new Vector3(side*(x+.16f),.48f,fore*z),new Vector3(.22f,.10f,.32f),Amber);
                }
            }
            bar.gameObject.SetActive(owner.Stats.lightBar);
            spikes.gameObject.SetActive(owner.Stats.spikeDamage>0);
        }
        void LateUpdate()
        {
            if(!lamp||!owner||owner.Stats==null)return;
            bool on=bar&&bar.gameObject.activeSelf&&owner.Damage&&!owner.Damage.IsDead&&TimeOfDay.Blend>.35f;
            lamp.enabled=on;
            if(on)lamp.intensity=8*Mathf.InverseLerp(.35f,.85f,TimeOfDay.Blend);
        }
    }
}
