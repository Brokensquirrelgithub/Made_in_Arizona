using UnityEngine;
namespace MadeInArizona
{
    /// <summary>Small background bird flocks with gliding and wingbeat movement.</summary>
    public sealed class DesertWildlife:MonoBehaviour
    {
        readonly Transform[] birds=new Transform[9];
        Vector3 center;bool ready;
        void Start()
        {
            var mat=WorldArt.Material(new Color(.13f,.14f,.12f));
            for(int i=0;i<birds.Length;i++)
            {
                var mesh=new SceneryMesh();mesh.Tri(new Vector3(-.65f,.05f,-.05f),new Vector3(0,0,.25f),new Vector3(-.2f,0,-.16f),new Color(.17f,.18f,.14f));mesh.Tri(new Vector3(.65f,.05f,-.05f),new Vector3(.2f,0,-.16f),new Vector3(0,0,.25f),new Color(.17f,.18f,.14f));
                var go=new GameObject("Circling raven");go.transform.SetParent(transform,false);mesh.Build(go,"Raven wings",mat);birds[i]=go.transform;
            }
        }
        void Update()
        {
            var game=GameManager.Instance;if(!game||!game.IsPlaying||!game.Player)return;
            if(!ready){center=game.Player.transform.position;ready=true;}
            center=Vector3.Lerp(center,game.Player.transform.position,Time.deltaTime*.08f);
            for(int i=0;i<birds.Length;i++)
            {
                float a=Time.time*(.12f+i*.007f)+i*.65f,r=21+i*2.8f;
                Vector3 p=center+new Vector3(Mathf.Sin(a)*r,0,Mathf.Cos(a)*r);
                p.y=GeneratedWorld.HeightAt(p)+16+i*.7f;
                birds[i].position=p;birds[i].rotation=Quaternion.Euler(Mathf.Sin(Time.time*7+i)*9,a*Mathf.Rad2Deg+90,Mathf.Sin(a)*13);
            }
        }
    }
}
