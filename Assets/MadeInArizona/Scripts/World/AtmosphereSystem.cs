using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace MadeInArizona
{
    /// <summary>Bounded heat refraction pools and artistic dust light shafts; high/ultra only.</summary>
    public sealed class AtmosphereSystem : MonoBehaviour
    {
        sealed class Haze { public Transform t; public Renderer renderer; public float until,start; }
        static AtmosphereSystem instance;
        readonly List<Haze> haze=new List<Haze>();
        readonly List<Transform> shafts=new List<Transform>();
        Material heat,shaft;MaterialPropertyBlock block;int quality;
        void Awake()
        {
            instance=this;quality=GameManager.Instance.Save.settings.quality;block=new MaterialPropertyBlock();
            var shader=Shader.Find("MadeInArizona/HeatHaze");if(shader) heat=new Material(shader);
            shader=Shader.Find("MadeInArizona/LightShaft");if(shader) {shaft=new Material(shader);shaft.SetColor("_BaseColor",new Color(1,.7f,.35f,.09f));}
        }
        public static AtmosphereSystem Create(Transform parent,bool garage)
        {
            var go=new GameObject("Heat, airborne dust and light scattering");go.transform.SetParent(parent);
            var system=go.AddComponent<AtmosphereSystem>();
            if(garage && system.quality>=2 && system.shaft) for(int i=-1;i<=1;i++) {
                var quad=system.Quad("Service bay airborne light",system.shaft);
                quad.transform.position=new Vector3(i*6,3.8f,3);quad.transform.localScale=new Vector3(7,7,1);system.shafts.Add(quad.transform);
            }
            return system;
        }
        GameObject Quad(string name,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Quad);go.name=name;go.transform.SetParent(transform);Destroy(go.GetComponent<Collider>());
            var renderer=go.GetComponent<Renderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;return go;
        }
        public static void Heat(Vector3 position,float radius,float seconds)
        {
            if(!instance||instance.quality<2||!instance.heat)return;
            Haze found=null;
            foreach(var item in instance.haze) if(!item.t.gameObject.activeSelf) {found=item;break;}
            if(found==null && instance.haze.Count<(instance.quality==3?32:12)) {
                var go=instance.Quad("Pooled thermal refraction",instance.heat);found=new Haze{t=go.transform,renderer=go.GetComponent<Renderer>()};instance.haze.Add(found);
            }
            if(found==null)return;
            found.t.gameObject.SetActive(true);found.t.position=position+Vector3.up*radius*.6f;found.t.localScale=new Vector3(radius*2,radius*2.5f,1);
            found.start=Time.time;found.until=Time.time+seconds;
        }
        void LateUpdate()
        {
            var camera=Camera.main;if(!camera)return;
            foreach(var item in haze) {
                if(!item.t.gameObject.activeSelf)continue;
                if(Time.time>item.until){item.t.gameObject.SetActive(false);continue;}
                item.t.rotation=camera.transform.rotation;
                block.SetFloat("_Opacity",Mathf.Clamp01((item.until-Time.time)/2));item.renderer.SetPropertyBlock(block);
            }
            foreach(var t in shafts) if(t)t.rotation=Quaternion.Euler(0,camera.transform.eulerAngles.y,0);
        }
        void OnDestroy(){if(instance==this)instance=null;if(heat)Destroy(heat);if(shaft)Destroy(shaft);}
    }
}
