using UnityEngine;
using static MadeInArizona.WorldArt;

namespace MadeInArizona
{
    /// <summary>
    /// The roster cars as low-poly versions of the cars that inspired them. Each is a <see cref="CarShell"/>
    /// lofted from that car's own proportions (overhangs, hood and deck lengths, roof height, glass rake), with its
    /// lamps, grille, bumpers and trim set on it. Heights are metres above the ground; the car's wheelbase and track
    /// come from its definition, so the model stands where the physics does.
    /// </summary>
    public static partial class VehicleVisual
    {
        const CarShell.Top Bodywork = CarShell.Top.Paint, Rooftop = CarShell.Top.Roof, Screen = CarShell.Top.Glass, Open = CarShell.Top.Tub;
        const CarShell.Side Flank = CarShell.Side.Paint, Window = CarShell.Side.Glass;
        /// <summary>
        /// Reflection curvature for a whole body in one mesh: its strength across the body, the height it curves about
        /// and the share of it applied along the body (see <see cref="WorldArt.CarPaint"/>).
        /// </summary>
        const float BodyPillow = 1f, BodyWaist = .7f, BodyLength = .45f;
        static readonly Color Glass = new Color(.09f, .2f, .25f), Silver = new Color(.74f, .76f, .78f), Chromium = new Color(.8f, .82f, .84f),
            Tyre = new Color(.035f, .041f, .039f), Trim = new Color(.045f, .05f, .055f), TailRed = new Color(.95f, .035f, .025f), Amber = new Color(1f, .5f, .08f);

        /// <summary>The function that shapes a roster car, or null for the boxed off-road rigs.</summary>
        static System.Action<Car> RosterBody(string id)
        {
            switch (id)
            {
                case "shift": return Shift;
                case "monsoon": return Monsoon;
                case "sonora": return Sonora;
                case "dustback": return Dustback;
                case "monument": return Monument;
                case "executive": return Executive;
                case "heat": return Heat;
                case "citizen": return Citizen;
                case "roam4": return Roam4;
                case "ae867": return Ae867;
                case "ronin": return Ronin;
                case "espresso": return Espresso;
                case "copperado": return Copperado;
                case "contender": return Contender;
                case "scarface": return Scarface;
                default: return null;
            }
        }

        /// <summary>Builds a roster car under <paramref name="root"/>; false for the boxed off-road rigs.</summary>
        static bool BuildRoster(VehicleDefinition definition, Transform root, bool enemy, EnemyFaction faction)
        {
            var build = RosterBody(definition.id);
            if (build == null) return false;
            var car = new Car(definition, root, enemy, faction);
            build(car);
            car.Finish();
            return true;
        }

        /// <summary>
        /// How far a roster car's tyres reach below its body origin. On its springs that is where the road is; a car held
        /// still off them (on the garage lift) has to be raised by this much to stand on its tyres instead of in the deck.
        /// </summary>
        public static float TyreDrop(VehicleDefinition definition) => RosterBody(definition.id) != null ? -GroundLevel(definition) : 0;

        /// <summary>Where the dog rides: the passenger seat. Open cars show her; closed ones keep her under the roof.</summary>
        public static Vector3 PassengerSeat(VehicleDefinition definition)
        {
            float ground = GroundLevel(definition);
            switch (definition.id)
            {
                case "heat": return new Vector3(.3f, ground + .46f, -.06f);
                case "espresso": return new Vector3(.3f, ground + .45f, -.4f);
                case "shift": case "citizen": case "ae867": case "monument": case "contender": return new Vector3(.3f, ground + .6f, -.1f);
                case "monsoon": case "sonora": case "executive": return new Vector3(.32f, ground + .68f, -.1f);
                case "dustback": return new Vector3(.32f, ground + .74f, -.1f);
                case "roam4": case "ronin": return new Vector3(.3f, ground + .9f, -.1f);
                case "copperado": return new Vector3(.4f, ground + 1.02f, .25f);
                case "scarface": return new Vector3(.3f, ground + .69f, -.1f);
                default: return new Vector3(.48f, 1.1f, .1f);
            }
        }

        /// <summary>
        /// Height of the ground in the model's space when the car rests on its springs (VehicleController.SupportSuspension):
        /// the suspension's rest length below the ray origin, less the sag that carries the car's weight.
        /// </summary>
        static float GroundLevel(VehicleDefinition definition)
        {
            float spring = Mathf.Clamp(definition.springStiffness / Mathf.Max(300, definition.mass), 20, 100);
            return -Mathf.Clamp(definition.rideHeight * .2f, .03f, .18f) + 9.81f / 4 / spring;
        }

        sealed class Car
        {
            public readonly VehicleDefinition definition; public readonly Transform root; public readonly bool enemy; public readonly EnemyFaction faction;
            public readonly CarShell shell = new CarShell();
            public readonly Color paint;
            public readonly Material painted, glazed, black, chrome;
            /// <summary>Ground level and the front and rear axle lines in model space.</summary>
            public readonly float ground, front, rear;
            float gunY = float.NaN, gunZ, plateY = .42f, plateProud = .02f, pipeY = .2f;
            int pipes = 1;

            public Car(VehicleDefinition definition, Transform root, bool enemy, EnemyFaction faction)
            {
                this.definition = definition; this.root = root; this.enemy = enemy; this.faction = faction;
                paint = enemy ? FactionRules.Paint(faction) : definition.color;
                ground = GroundLevel(definition); front = definition.wheelbase * .5f; rear = -front;
                painted = Paint(paint); glazed = CarGlass(Glass, BodyPillow, BodyWaist, BodyLength);
                // Bright trim is narrow: the usual tarnish and micro-scratches break a strip of it into dashes.
                black = Material(Trim, 0, .32f); chrome = Reflective(Chromium, SunGlint.Chrome, .93f, 1, .15f, .3f);
                shell.paint = shell.lower = shell.roof = shell.frame = painted; shell.glass = glazed;
                shell.dark = shell.tubWall = shell.tubFloor = Material(new Color(.03f, .034f, .04f));
            }

            /// <summary>z measured from the front axle (positive ahead of it) and from the rear axle.</summary>
            public float F(float ahead) => front + ahead;
            public float R(float ahead) => rear + ahead;
            /// <summary>Model-space height of a point <paramref name="height"/> metres above the ground.</summary>
            public float Y(float height) => ground + height;
            public float NoseZ => shell.NoseZ;
            public float TailZ => shell.TailZ;
            public float Half => shell.half;
            /// <summary>Body paint. Near-black paint takes a thinner clear coat: under the full one it mirrors the sky and reads as silver.</summary>
            public Material Paint(Color color) => CarPaint(color, .35f, color.grayscale < .1f ? .4f : 1, BodyPillow, BodyWaist, BodyLength);

            public void Body(float half, float rocker, float crease = .5f, float drop = .035f)
            {
                shell.half = half; shell.rocker = Y(rocker); shell.crease = crease; shell.drop = drop;
            }
            /// <summary>A station of the shell (see <see cref="CarShell.At"/>), with heights above the ground.</summary>
            public void S(float z, float belt, float crown, float edge, CarShell.Top over = Bodywork, CarShell.Side flank = Flank, bool pillar = false,
                float half = -1, float low = -1, float drop = -1, float floor = 0)
                => shell.At(z, Y(belt), Y(crown), edge, over, flank, pillar, half, low < 0 ? float.NaN : Y(low), drop, Y(floor));

            /// <summary>Half the distance between the front wheels' centres.</summary>
            public float FrontWheelX { get; private set; }

            /// <summary>
            /// Four wheels on the definition's wheelbase, tucked just inside the body sides, and the arches cut for them.
            /// <paramref name="skirt"/> caps the rear arch at that height (fender skirts). The front pair may be a
            /// different size, and <paramref name="openFront"/> stands them clear of the bodywork on off-road tread: no
            /// arch is cut, for a car with no front fenders.
            /// </summary>
            public void Wheels(float rearRadius, float rearWidth, Material rim, float rimSize = .62f, bool whitewall = false, float skirt = 0, float gap = .065f,
                float frontRadius = 0, float frontWidth = 0, bool openFront = false)
            {
                if (frontRadius <= 0) frontRadius = rearRadius;
                if (frontWidth <= 0) frontWidth = rearWidth;
                shell.wellDepth = Mathf.Max(rearWidth, frontWidth) + .1f;
                for (int axle = 1; axle >= -1; axle -= 2)
                {
                    bool nose = axle > 0, open = nose && openFront;
                    float radius = nose ? frontRadius : rearRadius, width = nose ? frontWidth : rearWidth;
                    // Behind a skirt the wheel sits further in, clear of the panel that covers it.
                    float z = nose ? front : rear, x = shell.half - width * .5f - .012f - (!nose && skirt > 0 ? .07f : 0);
                    if (nose) FrontWheelX = x;
                    if (!open) shell.Wheel(z, Y(radius), radius + gap, !nose && skirt > 0 ? Y(skirt) : float.MaxValue);
                    Beam("Axle", root, new Vector3(-x, Y(radius), z), new Vector3(x, Y(radius), z), .045f, Trim);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Transform wheel = Group(nose ? (side < 0 ? "Wheel_FL" : "Wheel_FR") : (side < 0 ? "Wheel_RL" : "Wheel_RR"), root, new Vector3(side * x, Y(radius), z));
                        Turned(Cylinder(open ? "All terrain tire" : "Road tire", wheel, Vector3.zero, radius, width, Tyre));
                        if (open)
                            for (int tread = 0; tread < 16; tread++)
                            {
                                float around = tread * Mathf.PI / 8;
                                var block = Box("Tread block", wheel, new Vector3(0, Mathf.Sin(around) * radius, Mathf.Cos(around) * radius), new Vector3(width + .02f, .05f, .08f), Tyre);
                                block.transform.localRotation = Quaternion.Euler(-around * Mathf.Rad2Deg, 0, 0);
                            }
                        float face = width * .5f;
                        if (whitewall) Turned(Cylinder("Whitewall", wheel, new Vector3(side * (face + .001f), 0, 0), radius * (rimSize + .16f), .012f, Cream));
                        Turned(Use(Cylinder("Wheel rim", wheel, new Vector3(side * (face + .004f), 0, 0), radius * rimSize, .03f, Silver), rim));
                        Turned(Cylinder("Hub", wheel, new Vector3(side * (face + .012f), 0, 0), radius * .17f, .034f, Trim));
                        // Slots in the rim show the wheel turning.
                        for (int slot = 0; slot < 5; slot++)
                        {
                            float angle = slot * Mathf.PI * .4f, reach = radius * rimSize * .62f;
                            var hole = Box("Wheel slot", wheel, new Vector3(side * (face + .019f), Mathf.Sin(angle) * reach, Mathf.Cos(angle) * reach), new Vector3(.012f, radius * .2f, radius * .11f), Trim);
                            hole.transform.localRotation = Quaternion.Euler(-angle * Mathf.Rad2Deg, 0, 0);
                        }
                    }
                }
            }
            static GameObject Turned(GameObject wheelPart) { wheelPart.transform.localRotation = Quaternion.Euler(0, 0, 90); return wheelPart; }

            /// <summary>A box on each side of the car (or one, on the centre line).</summary>
            public void Pair(string name, float x, float height, float z, Vector3 size, Material material, float glow = 0, Color? colour = null)
            {
                for (int side = x == 0 ? 1 : -1; side <= 1; side += 2)
                {
                    var box = Box(name, root, new Vector3(side * x, Y(height), z), size, colour ?? Trim, false, glow);
                    if (material) Use(box, material);
                }
            }
            /// <summary>Headlamps on the nose: lit lenses, which also place the beams (VehicleLights).</summary>
            public void Lamps(float x, float height, float width, float tall, bool round = false, float proud = .012f)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    var at = new Vector3(side * x, Y(height), NoseZ + proud);
                    if (round) Shape("Headlight", root, PrimitiveType.Cylinder, at, new Vector3(width, .02f, width), VehicleLights.HeadlightAmber, false, 0, .8f, 2.6f).transform.localRotation = Quaternion.Euler(90, 0, 0);
                    else Shape("Headlight", root, PrimitiveType.Cube, at, new Vector3(width, tall, .04f), VehicleLights.HeadlightAmber, false, 0, .8f, 2.6f);
                }
            }
            public void Tails(float x, float height, float width, float tall, float proud = .012f)
                => Pair("Tail light", x, height, TailZ - proud, new Vector3(width, tall, .04f), null, 1.4f, TailRed);
            public void Signals(float x, float height, float width, float tall)
                => Pair("Turn signal", x, height, NoseZ + .012f, new Vector3(width, tall, .04f), null, .5f, Amber);

            /// <summary>A bumper bar standing proud of the nose or tail and wrapping back along the sides.</summary>
            public void Bumper(bool nose, Material material, float low, float high, float proud = .05f, float wrap = .3f, float inset = .04f)
            {
                float tip = nose ? NoseZ + proud : TailZ - proud, body = nose ? NoseZ - wrap : TailZ + wrap;
                shell.Bar(material, tip, body, Y(low), Y(high), shell.HalfAt(nose ? NoseZ : TailZ) - inset + proud, shell.HalfAt(body) + proud * .6f, .12f);
                if (!nose) plateProud = Mathf.Max(plateProud, proud + .02f);
            }
            public void Grille(string name, float height, float width, float tall, Material material, float proud = .01f)
                => Use(Box(name, root, new Vector3(0, Y(height), NoseZ + proud), new Vector3(width, tall, .04f), Trim), material);
            /// <summary>Door mirrors on the beltline at <paramref name="z"/>.</summary>
            public void Mirrors(float z, bool bodyColour = true)
            {
                float x = shell.ShoulderAt(z) + .04f, height = shell.BeltAt(z) - ground + .05f;
                Pair("Wing mirror", x, height, z, new Vector3(.11f, .09f, .06f), bodyColour ? painted : black);
            }
            public void Handles(float height, params float[] zs)
            {
                foreach (float z in zs) Pair("Door handle", shell.HalfAt(z) - .005f, height, z, new Vector3(.03f, .028f, .13f), chrome);
            }
            /// <summary>Door shut lines at each z.</summary>
            public void Doors(params float[] zs) { foreach (float z in zs) shell.Seam(black, z); }

            /// <summary>
            /// A flat part lying on the top of the body (hood, roof or deck) from <paramref name="z0"/> back to
            /// <paramref name="z1"/>, pitched to the surface under it.
            /// </summary>
            public GameObject OnTop(string name, float x, float z0, float z1, float width, float thick, Material material, float lift = 0)
            {
                float y0 = shell.CrownAt(z0) + lift, y1 = shell.CrownAt(z1) + lift;
                var a = new Vector3(x, y0, z0); var b = new Vector3(x, y1, z1);
                var box = Use(Box(name, root, (a + b) * .5f + Vector3.up * thick * .5f, new Vector3(width, thick, Vector3.Distance(a, b)), Trim), material);
                box.transform.localRotation = Quaternion.LookRotation(a - b, Vector3.up);
                return box;
            }
            public void OnTopPair(string name, float x, float z0, float z1, float width, float thick, Material material, float lift = 0)
            {
                OnTop(name, x, z0, z1, width, thick, material, lift); OnTop(name, -x, z0, z1, width, thick, material, lift);
            }

            /// <summary>A spare wheel on the tailgate.</summary>
            public void Spare(string name, float height, float radius, float width, Material cover = null)
            {
                var spare = Cylinder(name, root, new Vector3(0, Y(height), TailZ - width * .5f - .03f), radius, width, Tyre);
                spare.transform.localRotation = Quaternion.Euler(90, 0, 0);
                var face = Cylinder(name + " cover", root, new Vector3(0, Y(height), TailZ - width - .035f), radius * .66f, .03f, Silver);
                face.transform.localRotation = Quaternion.Euler(90, 0, 0);
                if (cover) Use(face, cover);
                plateProud = Mathf.Max(plateProud, width + .07f);
            }

            /// <summary>
            /// The windscreen of an open car: a framed pane standing on the cowl at <paramref name="z0"/>, raked back to
            /// <paramref name="z1"/> at <paramref name="top"/> metres above the ground.
            /// </summary>
            public void Windscreen(float z0, float z1, float top, float halfTop)
            {
                float halfBase = shell.EdgeAt(z0), baseY = shell.CrownAt(z0) - .01f;
                Vector3 a = new Vector3(0, baseY, z0), b = new Vector3(halfBase, baseY, z0), c = new Vector3(halfTop, Y(top), z1), d = new Vector3(0, Y(top), z1);
                Vector3 outward = Vector3.Cross(b - a, d - a); if (outward.z < 0) outward = -outward;
                shell.Pane(a, b, c, d, 0, .045f, .03f, .045f, outward);
                shell.Pane(a, b, c, d, 0, .045f, .03f, .045f, -outward);
            }

            /// <summary>Two bucket seats in an open cockpit, upholstered in <paramref name="fabric"/>.</summary>
            public void Seats(string name, float z, float floor, Material fabric)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = side * .3f;
                    Box("Seat cushion", root, new Vector3(x, Y(floor + .07f), z + .06f), new Vector3(.4f, .14f, .42f), Trim);
                    var back = Group("Seat back", root, new Vector3(x, Y(floor + .14f), z - .16f));
                    back.localRotation = Quaternion.Euler(-14, 0, 0);
                    Box("Seat shell", back, new Vector3(0, .24f, 0), new Vector3(.4f, .5f, .1f), Trim);
                    Box("Head rest", back, new Vector3(0, .55f, 0), new Vector3(.24f, .14f, .09f), Trim);
                    // The pattern is the fabric's texture: one panel on the cushion, one on the backrest.
                    Use(Box(name, root, new Vector3(x, Y(floor + .143f), z + .07f), new Vector3(.32f, .012f, .34f), Cream), fabric);
                    Use(Box(name, back, new Vector3(0, .23f, .053f), new Vector3(.32f, .4f, .012f), Cream), fabric);
                }
            }

            /// <summary>Where the turret's ring sits: <paramref name="height"/> above the ground at <paramref name="z"/>.</summary>
            public void Gun(float height, float z) { gunY = Y(height); gunZ = z; }
            /// <summary>Roll hoops behind an open cockpit, with a plate across them for the turret.</summary>
            public void GunHoop(float z, float foot, float top, float halfWidth)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Use(Beam("Roll hoop", root, new Vector3(side * halfWidth, Y(foot), z), new Vector3(side * halfWidth * .86f, Y(top), z), .03f, Trim), black);
                    Use(Beam("Roll hoop brace", root, new Vector3(side * halfWidth * .86f, Y(top), z), new Vector3(side * halfWidth * .8f, Y(foot), z - .32f), .022f, Trim), black);
                }
                Use(Beam("Roll hoop", root, new Vector3(-halfWidth * .86f, Y(top), z), new Vector3(halfWidth * .86f, Y(top), z), .03f, Trim), black);
                Use(Box("Turret plate", root, new Vector3(0, Y(top + .03f), z - .04f), new Vector3(.5f, .03f, .46f), Trim), black);
                Gun(top + .045f, z - .04f);
            }
            public void Plate(float height, float proud = 0) { plateY = height; plateProud = Mathf.Max(plateProud, proud); }
            public void Exhaust(int count, float height) { pipes = count; pipeY = height; }

            public void Finish()
            {
                shell.Skin();
                shell.Emit(root, "Body");
                float width = shell.half * 2, length = NoseZ - TailZ;
                // Tailpipes under the rear valance; nitro flames leave from each mouth (see NitroExhaust).
                for (int pipe = 0; pipe < pipes; pipe++)
                {
                    float x = (pipes == 1 ? 1 : pipe == 0 ? -1 : 1) * shell.half * .5f, end = TailZ - .07f;
                    var steel = new Color(.6f, .61f, .58f);
                    Use(Beam("Tailpipe", root, new Vector3(x, Y(pipeY), TailZ + .3f), new Vector3(x, Y(pipeY), end), .034f, steel), Chrome(steel, .82f, 1.5f));
                    Cylinder("Tailpipe mouth", root, new Vector3(x, Y(pipeY), end - .004f), .026f, .016f, Ink).transform.localRotation = Quaternion.Euler(90, 0, 0);
                    Group(NitroExhaust.ExitName, root, new Vector3(x, Y(pipeY), end - .03f)).localRotation = Quaternion.Euler(0, 180, 0);
                }
                if (enemy)
                    for (int side = -1; side <= 1; side += 2)
                        Box("Faction stripe", root, new Vector3(side * (shell.HalfAt(0) + .012f), shell.BeltAt(0) - .14f, 0), new Vector3(.02f, .09f, length * .3f), FactionRules.Accent(faction), false, 1.1f);
                if (float.IsNaN(gunY)) { gunZ = -.15f; gunY = shell.CrownAt(gunZ); }
                Outfit(root, enemy, faction, width, gunY, gunZ, TailZ + .14f, new Vector3(0, Y(plateY), TailZ - plateProud - .012f), new Vector3(0, gunY + .08f, gunZ));
            }
        }

        // 1991 Geo Metro three-door: a short wedge nose, a long glasshouse and an upright glass hatch.
        static void Shift(Car c)
        {
            c.Body(.78f, .21f, .5f, .04f);
            c.S(c.F(.62f), .55f, .61f, .54f, half: .68f, low: .27f);
            c.S(c.F(.46f), .63f, .68f, .62f);
            c.S(c.F(-.38f), .84f, .89f, .65f, Screen, Window);
            c.S(c.F(-.98f), .85f, 1.36f, .55f, Rooftop, Window);
            c.S(-.33f, .85f, 1.375f, .56f, Rooftop, Window, true);
            c.S(c.R(-.10f), .86f, 1.345f, .55f, Screen, Flank, true);
            c.S(c.R(-.52f), .82f, .88f, .62f);
            c.S(c.R(-.62f), .76f, .82f, .56f, half: .70f, low: .29f);
            c.Wheels(.285f, .17f, Chrome(Silver, .7f, 1.3f), .6f);
            c.Lamps(.47f, .5f, .3f, .11f);
            c.Signals(.5f, .37f, .2f, .05f);
            c.Grille("Shift grille slot", .5f, .5f, .05f, c.black);
            c.Grille("Shift lower intake", .33f, .7f, .07f, c.black);
            c.Tails(.5f, .64f, .3f, .13f);
            c.Pair("Shift tail strip", 0, .64f, c.TailZ - .008f, new Vector3(.66f, .09f, .03f), c.black);
            c.shell.Strake(c.black, c.F(-.42f), c.R(.42f), .5f, .05f);
            c.Doors(c.F(-.36f), -.36f);
            c.Mirrors(c.F(-.5f), false);
            c.Handles(.74f, -.2f);
            c.Gun(1.375f, -.4f);
            c.Plate(.42f); c.Exhaust(1, .24f);
        }

        // 2007 Volkswagen Jetta: a tall-tailed three-box sedan with a high beltline and a shield grille.
        static void Monsoon(Car c)
        {
            c.Body(.86f, .22f, .62f, .045f);
            c.S(c.F(.76f), .62f, .7f, .6f, half: .74f, low: .28f);
            c.S(c.F(.58f), .73f, .79f, .7f);
            c.S(c.F(-.42f), .98f, 1.02f, .72f, Screen, Window);
            c.S(c.F(-1.02f), .99f, 1.46f, .6f, Rooftop, Window);
            c.S(-.22f, 1f, 1.48f, .61f, Rooftop, Window, true);
            c.S(c.R(.42f), 1.01f, 1.455f, .6f, Screen, Flank, true);
            c.S(c.R(-.2f), 1.03f, 1.1f, .7f);
            c.S(c.R(-.72f), 1.01f, 1.08f, .68f);
            c.S(c.R(-.84f), .93f, .99f, .6f, half: .76f, low: .3f);
            c.Wheels(.32f, .2f, c.chrome, .64f);
            c.Lamps(.52f, .62f, .3f, .13f);
            c.Grille("Monsoon shield grille", .5f, .46f, .3f, Patterned(CarTextures.Slats, new Color(.2f, .21f, .22f), new Vector2(1, 5), .4f));
            c.Pair("Monsoon grille surround", 0, .5f, c.NoseZ + .006f, new Vector3(.52f, .36f, .03f), c.chrome);
            c.Signals(.56f, .38f, .2f, .06f);
            c.Tails(.56f, .84f, .3f, .2f);
            c.Pair("Monsoon trunk strip", 0, .95f, c.TailZ - .008f, new Vector3(.5f, .03f, .03f), c.chrome);
            c.Doors(c.F(-.44f), -.24f, c.R(.5f));
            c.Mirrors(c.F(-.56f));
            c.Handles(.9f, -.02f, c.R(.72f));
            c.Gun(1.48f, -.3f);
            c.Plate(.62f); c.Exhaust(1, .25f);
        }

        // 2001 Hyundai Sonata: a soft, rounded family sedan with a fast windscreen and a small bright grille.
        static void Sonora(Car c)
        {
            c.Body(.89f, .22f, .55f, .055f);
            c.S(c.F(.78f), .56f, .64f, .58f, half: .72f, low: .28f);
            c.S(c.F(.58f), .68f, .74f, .7f);
            c.S(c.F(-.38f), .93f, .98f, .74f, Screen, Window);
            c.S(c.F(-1.06f), .95f, 1.41f, .6f, Rooftop, Window);
            c.S(-.18f, .96f, 1.44f, .62f, Rooftop, Window, true);
            c.S(c.R(.52f), .97f, 1.4f, .6f, Screen, Flank, true);
            c.S(c.R(-.22f), .99f, 1.05f, .7f);
            c.S(c.R(-.74f), .96f, 1.02f, .66f);
            c.S(c.R(-.88f), .86f, .93f, .58f, half: .74f, low: .3f);
            c.Wheels(.325f, .2f, c.chrome, .62f);
            c.Lamps(.5f, .57f, .28f, .12f);
            c.Grille("Sonora satin grille", .55f, .4f, .11f, Patterned(CarTextures.Slats, new Color(.75f, .77f, .78f), new Vector2(1, 4), .8f, 1));
            c.Grille("Sonora lower intake", .36f, .8f, .08f, c.black);
            c.Signals(.6f, .44f, .14f, .05f);
            c.Tails(.52f, .8f, .34f, .16f);
            c.Doors(c.F(-.4f), -.2f, c.R(.6f));
            c.Mirrors(c.F(-.52f));
            c.Handles(.86f, 0, c.R(.8f));
            c.Gun(1.44f, -.25f);
            c.Plate(.6f); c.Exhaust(1, .25f);
        }

        // 2002 Subaru Outback: a raised wagon in sapphire over titanium, with roof rails and a raked tailgate.
        static void Dustback(Car c)
        {
            Material titanium = c.enemy ? c.painted : c.Paint(new Color(.56f, .55f, .52f));
            c.Body(.88f, .28f, .42f, .04f);
            c.shell.lower = titanium;
            c.S(c.F(.78f), .66f, .73f, .62f, half: .76f, low: .34f);
            c.S(c.F(.6f), .78f, .83f, .72f);
            c.S(c.F(-.4f), 1.02f, 1.06f, .74f, Screen, Window);
            c.S(c.F(-1.02f), 1.03f, 1.52f, .62f, Rooftop, Window);
            c.S(-.2f, 1.04f, 1.54f, .63f, Rooftop, Window, true);
            c.S(c.R(.34f), 1.04f, 1.535f, .63f, Rooftop, Window, true);
            c.S(c.R(-.46f), 1.05f, 1.5f, .61f, Screen, Flank, true);
            c.S(c.R(-.84f), 1.02f, 1.08f, .68f);
            c.S(c.R(-.92f), .96f, 1.02f, .62f, half: .78f, low: .36f);
            c.Wheels(.34f, .21f, c.chrome, .62f, gap: .075f);
            c.Lamps(.52f, .66f, .3f, .13f);
            c.Grille("Dustback grille", .66f, .42f, .11f, Patterned(CarTextures.Slats, new Color(.22f, .23f, .24f), new Vector2(1, 3), .4f));
            c.Pair("Dustback fog lamp", .5f, .46f, c.NoseZ + .012f, new Vector3(.15f, .09f, .04f), null, 1.6f, new Color(1, .9f, .7f));
            c.Tails(.56f, .9f, .28f, .2f);
            c.Pair("Dustback roof rail", .5f, 1.6f, -.34f, new Vector3(.04f, .04f, 1.75f), c.black);
            c.Pair("Dustback rail foot", .5f, 1.56f, .4f, new Vector3(.05f, .07f, .1f), c.black);
            c.Pair("Dustback rail foot", .5f, 1.56f, -1.1f, new Vector3(.05f, .07f, .1f), c.black);
            c.OnTop("Dustback tailgate spoiler", 0, c.R(-.4f), c.R(-.52f), 1.1f, .04f, c.painted);
            c.Doors(c.F(-.42f), -.22f, c.R(.42f));
            c.Mirrors(c.F(-.54f));
            c.Handles(.94f, 0, c.R(.6f));
            c.Gun(1.54f, -.3f);
            c.Plate(.68f); c.Exhaust(1, .31f);
        }

        // 1990 Acura Legend sedan: low and crisp, a long thin nose, big glass and a full-width tail lamp.
        static void Monument(Car c)
        {
            c.Body(.9f, .21f, .5f, .025f);
            c.S(c.F(.82f), .54f, .6f, .68f, half: .82f, low: .26f);
            c.S(c.F(.68f), .62f, .66f, .76f);
            c.S(c.F(-.4f), .87f, .9f, .78f, Screen, Window);
            c.S(c.F(-1f), .88f, 1.37f, .66f, Rooftop, Window);
            c.S(-.22f, .89f, 1.39f, .67f, Rooftop, Window, true);
            c.S(c.R(.46f), .9f, 1.37f, .66f, Screen, Flank, true);
            c.S(c.R(-.14f), .93f, .98f, .76f);
            c.S(c.R(-.8f), .92f, .97f, .75f);
            c.S(c.R(-.92f), .86f, .92f, .7f, half: .84f, low: .28f);
            c.Wheels(.32f, .2f, c.chrome, .62f);
            c.Lamps(.54f, .53f, .4f, .09f);
            c.Grille("Monument bright grille", .53f, .34f, .08f, Patterned(CarTextures.Slats, new Color(.8f, .8f, .78f), new Vector2(1, 3), .85f, 1));
            c.Signals(.6f, .4f, .26f, .05f);
            c.Grille("Monument lower intake", .32f, .7f, .06f, c.black);
            c.Pair("Monument tail lamp bar", 0, .8f, c.TailZ - .012f, new Vector3(1.5f, .12f, .04f), null, 1.2f, TailRed);
            c.Pair("Monument hood crest", 0, .67f, c.F(.6f), new Vector3(.05f, .03f, .07f), c.chrome);
            c.shell.Strake(c.black, c.F(-.4f), c.R(.44f), .5f, .035f);
            c.Doors(c.F(-.42f), -.24f, c.R(.54f));
            c.Mirrors(c.F(-.54f));
            c.Handles(.8f, -.02f, c.R(.76f));
            c.Gun(1.39f, -.3f);
            c.Plate(.6f); c.Exhaust(2, .24f);
        }

        // 1990 Cadillac Sedan de Ville: long, square and formal, with skirted rear wheels, blade tail lamps and chrome.
        static void Executive(Car c)
        {
            c.Body(.93f, .22f, .5f, .02f);
            c.shell.roof = c.enemy ? c.painted : Material(new Color(.02f, .021f, .024f), 0, .12f);
            c.S(c.F(.86f), .72f, .78f, .78f, half: .88f, low: .32f);
            c.S(c.F(.76f), .79f, .83f, .82f);
            c.S(c.F(-.46f), .96f, .99f, .84f, Screen, Window);
            c.S(c.F(-.96f), .97f, 1.41f, .72f, Rooftop, Window);
            c.S(-.26f, .97f, 1.425f, .73f, Rooftop, Window, true);
            c.S(c.R(.36f), .97f, 1.42f, .73f, Rooftop, Flank, true);
            c.S(c.R(.02f), .97f, 1.4f, .72f, Screen, Flank);
            c.S(c.R(-.16f), .98f, 1.02f, .82f);
            c.S(c.R(-.92f), .96f, 1f, .82f);
            c.S(c.R(-1f), .88f, .94f, .78f, half: .88f, low: .34f);
            c.Wheels(.335f, .2f, c.chrome, .56f, true, .5f);
            c.Bumper(true, c.chrome, .36f, .5f, .06f, .34f);
            c.Bumper(false, c.chrome, .38f, .52f, .06f, .34f);
            c.Grille("Executive eggcrate grille", .65f, .62f, .2f, Patterned(CarTextures.Eggcrate, Chromium, new Vector2(12, 4), .88f, 1));
            c.Lamps(.62f, .65f, .42f, .13f);
            c.Signals(.62f, .55f, .42f, .04f);
            c.Pair("Executive hood ornament", 0, .87f, c.F(.72f), new Vector3(.025f, .09f, .025f), c.chrome);
            c.Pair("Tail light", .82f, .78f, c.TailZ - .012f, new Vector3(.1f, .34f, .05f), null, 1.4f, TailRed);
            c.Pair("Executive tail panel", 0, .72f, c.TailZ - .008f, new Vector3(1.3f, .12f, .03f), c.chrome);
            c.shell.Strake(c.chrome, c.F(.6f), c.R(-.8f), .5f, .03f);
            c.shell.Strake(c.chrome, c.F(-.5f), c.R(.5f), .04f, .05f);
            c.Doors(c.F(-.48f), -.28f, c.R(.44f));
            c.Mirrors(c.F(-.58f), false);
            c.Handles(.88f, -.06f, c.R(.66f));
            c.Gun(1.425f, -.3f);
            c.Plate(.66f); c.Exhaust(1, .26f);
        }

        // 1991 Honda Beat: a mid-engine kei roadster. Cab forward, side intakes ahead of the rear wheels, zebra seats.
        static void Heat(Car c)
        {
            c.Body(.72f, .19f, .5f, .035f);
            c.S(c.F(.5f), .47f, .53f, .5f, half: .62f, low: .24f);
            c.S(c.F(.36f), .55f, .6f, .56f);
            c.S(c.F(-.22f), .75f, .79f, .58f);
            c.S(c.F(-.38f), .78f, .79f, .6f, Open, drop: 0, floor: .3f);
            c.S(c.R(.68f), .8f, .81f, .6f, Bodywork, drop: 0, floor: .3f);
            c.S(c.R(.54f), .82f, .88f, .58f);
            c.S(c.R(-.4f), .78f, .82f, .58f);
            c.S(c.R(-.52f), .7f, .76f, .52f, half: .64f, low: .26f);
            c.Wheels(.28f, .16f, Chrome(Silver, .7f, 1.3f), .6f);
            c.Windscreen(c.F(-.22f), c.F(-.72f), 1.13f, .5f);
            c.Seats("Heat zebra seat fabric", -.1f, .3f, Patterned(CarTextures.Zebra, Color.white, Vector2.one, .2f));
            c.Pair("Dashboard", 0, .72f, c.F(-.34f), new Vector3(1.1f, .12f, .12f), c.black);
            var wheel = Cylinder("Steering wheel", c.root, new Vector3(-.3f, c.Y(.72f), c.F(-.5f)), .15f, .025f, Trim); wheel.transform.localRotation = Quaternion.Euler(70, 0, 0);
            c.Lamps(.42f, .46f, .2f, .1f);
            c.Signals(.44f, .35f, .16f, .04f);
            c.Grille("Heat lower intake", .31f, .5f, .06f, c.black);
            c.Tails(.44f, .62f, .24f, .09f);
            c.OnTop("Heat engine cover", 0, c.R(.4f), c.R(-.3f), .62f, .012f, Patterned(CarTextures.Slats, new Color(.09f, .1f, .11f), new Vector2(1, 7), .3f), .002f);
            // The side intake ahead of each rear wheel, with the groove that runs back to it from the door.
            c.Pair("Heat side intake", c.Half - .01f, .5f, c.R(.5f), new Vector3(.04f, .16f, .12f), c.black);
            c.shell.Strake(c.black, c.F(-.5f), c.R(.56f), .52f, .016f, .005f);
            c.Doors(c.F(-.4f), c.R(.72f));
            c.Mirrors(c.F(-.42f));
            c.GunHoop(c.R(.6f), .8f, 1.08f, .52f);
            c.Plate(.4f); c.Exhaust(2, .22f);
        }

        // 1993 Honda Civic hatchback: a very low nose, a deep raked windscreen and a rounded glass hatch.
        static void Citizen(Car c)
        {
            c.Body(.84f, .2f, .5f, .05f);
            c.S(c.F(.74f), .52f, .58f, .56f, half: .7f, low: .25f);
            c.S(c.F(.56f), .6f, .65f, .66f);
            c.S(c.F(-.36f), .82f, .86f, .7f, Screen, Window);
            c.S(c.F(-1.1f), .83f, 1.34f, .58f, Rooftop, Window);
            c.S(-.28f, .84f, 1.36f, .59f, Rooftop, Window, true);
            c.S(c.R(.12f), .85f, 1.33f, .58f, Screen, Flank, true);
            c.S(c.R(-.5f), .84f, .9f, .66f);
            c.S(c.R(-.66f), .76f, .82f, .58f, half: .74f, low: .28f);
            c.Wheels(.3f, .19f, c.chrome, .6f);
            c.Lamps(.5f, .5f, .34f, .09f);
            c.Grille("Citizen grille slot", .5f, .34f, .04f, c.black);
            c.Grille("Citizen lower intake", .32f, .66f, .07f, c.black);
            c.Signals(.56f, .36f, .18f, .05f);
            c.Tails(.6f, .72f, .16f, .2f);
            c.Pair("Citizen rear lamp bridge", 0, .7f, c.TailZ - .01f, new Vector3(.9f, .07f, .03f), null, .5f, new Color(.45f, .03f, .035f));
            c.OnTop("Citizen hatch spoiler", 0, c.R(.16f), c.R(.02f), 1.08f, .035f, c.painted);
            c.Doors(c.F(-.38f), -.3f);
            c.Mirrors(c.F(-.5f));
            c.Handles(.74f, -.14f);
            c.Gun(1.36f, -.4f);
            c.Plate(.44f); c.Exhaust(1, .23f);
        }

        // 2005 Toyota RAV4: a tall, short crossover with grey cladding and the spare on its side-hinged door.
        static void Roam4(Car c)
        {
            Material cladding = c.enemy ? c.painted : Material(new Color(.3f, .31f, .32f), 0, .3f);
            c.Body(.87f, .34f, .36f, .04f);
            c.shell.lower = cladding;
            c.S(c.F(.74f), .76f, .84f, .64f, half: .76f, low: .42f);
            c.S(c.F(.58f), .88f, .93f, .72f);
            c.S(c.F(-.34f), 1.08f, 1.12f, .74f, Screen, Window);
            c.S(c.F(-.92f), 1.09f, 1.64f, .64f, Rooftop, Window);
            c.S(-.16f, 1.1f, 1.67f, .65f, Rooftop, Window, true);
            c.S(c.R(.3f), 1.1f, 1.67f, .65f, Rooftop, Window, true);
            c.S(c.R(-.44f), 1.11f, 1.64f, .64f, Screen, Flank, true);
            c.S(c.R(-.6f), 1.08f, 1.14f, .7f);
            c.S(c.R(-.66f), 1f, 1.08f, .66f, half: .8f, low: .44f);
            c.Wheels(.36f, .22f, c.chrome, .6f, gap: .08f);
            c.shell.Flare(cladding, c.front, .07f, .025f); c.shell.Flare(cladding, c.rear, .07f, .025f);
            c.Lamps(.5f, .78f, .34f, .16f);
            c.Grille("Roam4 grille", .78f, .36f, .12f, Patterned(CarTextures.Slats, new Color(.2f, .21f, .22f), new Vector2(1, 3), .4f));
            c.Pair("Roam4 fog lamp", .52f, .52f, c.NoseZ + .012f, new Vector3(.13f, .08f, .04f), null, 1.6f, new Color(1, .9f, .7f));
            c.Tails(.66f, 1f, .14f, .3f);
            c.Spare("Roam4 rear spare", .98f, .34f, .2f, c.painted);
            c.Pair("Roam4 roof rail", .52f, 1.72f, -.3f, new Vector3(.04f, .04f, 1.3f), c.black);
            c.Doors(c.F(-.36f), -.18f, c.R(.38f));
            c.Mirrors(c.F(-.46f), false);
            c.Handles(1f, .02f, c.R(.56f));
            c.Gun(1.67f, -.2f);
            c.Plate(.62f); c.Exhaust(1, .37f);
        }

        // 1985 Toyota Sprinter Trueno: a sharp wedge with pop-up lamps and a long glass liftback, white over black.
        static void Ae867(Car c)
        {
            c.Body(.82f, .2f, .42f, .03f);
            c.shell.lower = c.enemy ? c.painted : c.Paint(new Color(.04f, .042f, .046f));
            c.S(c.F(.78f), .47f, .51f, .62f, half: .74f, low: .24f);
            c.S(c.F(.62f), .55f, .585f, .7f);
            c.S(c.F(-.52f), .82f, .86f, .7f, Screen, Window);
            c.S(c.F(-1.12f), .83f, 1.325f, .58f, Rooftop, Window);
            c.S(-.22f, .84f, 1.34f, .59f, Rooftop, Window, true);
            c.S(c.R(.62f), .85f, 1.32f, .58f, Screen, Window, true);
            c.S(c.R(.06f), .87f, 1.115f, .62f, Screen, Flank, true);
            c.S(c.R(-.5f), .88f, .92f, .68f);
            c.S(c.R(-.84f), .84f, .9f, .66f, half: .76f, low: .26f);
            c.Wheels(.29f, .19f, Chrome(new Color(.3f, .31f, .33f), .6f, 1.2f), .66f);
            // Pop-up lamps, raised.
            for (int side = -1; side <= 1; side += 2)
            {
                float z = c.F(.58f), y = c.shell.CrownAt(z);
                Box("AE 867 pop-up lamp", c.root, new Vector3(side * .52f, y + .04f, z), new Vector3(.3f, .12f, .22f), Trim).GetComponent<Renderer>().sharedMaterial = c.shell.lower;
                Shape("Headlight", c.root, PrimitiveType.Cube, new Vector3(side * .52f, y + .045f, z + .115f), new Vector3(.24f, .08f, .02f), VehicleLights.HeadlightAmber, false, 0, .8f, 2.6f);
            }
            c.Signals(.54f, .4f, .3f, .05f);
            c.Grille("AE 867 intake", .3f, .7f, .06f, c.black);
            c.Pair("Tail light", .5f, .72f, c.TailZ - .012f, new Vector3(.46f, .11f, .04f), null, 1.4f, TailRed);
            c.Pair("AE 867 black rear garnish", 0, .72f, c.TailZ - .008f, new Vector3(.56f, .11f, .03f), c.black);
            c.OnTop("AE 867 tail spoiler", 0, c.R(-.66f), c.R(-.84f), 1.3f, .05f, c.shell.lower, .01f);
            c.Doors(c.F(-.54f), -.24f);
            c.Mirrors(c.F(-.64f), false);
            c.Handles(.74f, -.1f);
            c.Gun(1.34f, -.3f);
            c.Plate(.44f); c.Exhaust(1, .23f);
        }

        // 1989 Suzuki Samurai: a tall narrow box on big wheels, with square flares, round lamps and the spare on the back.
        static void Ronin(Car c)
        {
            c.Body(.76f, .44f, .5f, .015f);
            c.shell.archSides = 4;
            c.S(c.F(.58f), .9f, .96f, .62f, half: .72f, low: .52f);
            c.S(c.F(.5f), .97f, .99f, .66f);
            c.S(c.F(-.44f), 1.01f, 1.03f, .68f, Screen, Window);
            c.S(c.F(-.6f), 1.02f, 1.64f, .64f, Rooftop, Window);
            c.S(-.14f, 1.02f, 1.66f, .65f, Rooftop, Window, true);
            c.S(c.R(-.46f), 1.02f, 1.65f, .64f, Screen, Flank, true);
            c.S(c.R(-.54f), 1f, 1.04f, .68f);
            c.S(c.R(-.58f), .96f, 1f, .66f, half: .74f, low: .54f);
            c.Wheels(.36f, .2f, c.enemy ? c.chrome : c.Paint(new Color(.9f, .9f, .86f)), .58f, gap: .09f);
            c.shell.Flare(c.black, c.front, .08f, .05f); c.shell.Flare(c.black, c.rear, .08f, .05f);
            c.Bumper(true, c.black, .52f, .66f, .08f, .16f);
            c.Bumper(false, c.black, .54f, .66f, .06f, .14f);
            c.Lamps(.44f, .8f, .17f, .17f, true);
            c.Grille("Ronin slat grille", .8f, .52f, .18f, Patterned(CarTextures.Bars, new Color(.12f, .13f, .14f), new Vector2(7, 1), .3f));
            c.Signals(.46f, .66f, .14f, .05f);
            c.Tails(.56f, .8f, .1f, .16f);
            c.Spare("Ronin rear spare", .98f, .36f, .2f, c.enemy ? c.chrome : c.Paint(new Color(.9f, .9f, .86f)));
            c.shell.Strake(c.black, c.F(-.5f), c.R(.5f), .02f, .09f);
            c.Doors(c.F(-.46f), -.16f);
            c.Mirrors(c.F(-.5f), false);
            c.Handles(.94f, -.02f);
            c.Gun(1.66f, -.4f);
            c.Plate(.6f); c.Exhaust(1, .47f);
        }

        // 1991 Suzuki Cappuccino: a front-engine kei roadster. A long hood, the cockpit against the rear axle, a targa hoop.
        static void Espresso(Car c)
        {
            c.Body(.72f, .19f, .5f, .035f);
            c.S(c.F(.62f), .45f, .51f, .5f, half: .6f, low: .24f);
            c.S(c.F(.46f), .53f, .58f, .56f);
            c.S(c.F(-.62f), .74f, .78f, .58f);
            c.S(c.F(-.78f), .77f, .78f, .6f, Open, drop: 0, floor: .29f);
            c.S(c.R(.22f), .79f, .8f, .6f, Bodywork, drop: 0, floor: .29f);
            c.S(c.R(.1f), .8f, .83f, .58f);
            c.S(c.R(-.5f), .76f, .8f, .58f);
            c.S(c.R(-.62f), .68f, .74f, .52f, half: .62f, low: .26f);
            c.Wheels(.275f, .165f, c.chrome, .64f);
            c.Windscreen(c.F(-.62f), c.F(-1.08f), 1.12f, .5f);
            c.Seats("Espresso seat fabric", -.44f, .29f, Material(new Color(.16f, .15f, .15f), 0, .2f));
            c.Pair("Dashboard", 0, .71f, c.F(-.74f), new Vector3(1.1f, .12f, .12f), c.black);
            var wheel = Cylinder("Steering wheel", c.root, new Vector3(-.3f, c.Y(.71f), c.F(-.9f)), .15f, .025f, Trim); wheel.transform.localRotation = Quaternion.Euler(70, 0, 0);
            c.Lamps(.42f, .47f, .15f, .15f, true);
            c.Signals(.44f, .35f, .14f, .04f);
            c.Grille("Espresso grille mouth", .32f, .44f, .08f, c.black);
            c.Pair("Espresso front-engine scoop", 0, c.shell.CrownAt(c.F(-.2f)) - c.ground + .012f, c.F(-.2f), new Vector3(.3f, .03f, .36f), c.painted);
            c.Tails(.44f, .6f, .22f, .1f);
            // The fixed hoop behind the seats, glazed across the back: the roof panels lift out between it and the screen.
            for (int side = -1; side <= 1; side += 2)
                Use(Box("Espresso targa hoop", c.root, new Vector3(side * .56f, c.Y(.96f), c.R(.2f)), new Vector3(.06f, .32f, .1f), Trim), c.painted);
            Use(Box("Espresso targa hoop", c.root, new Vector3(0, c.Y(1.125f), c.R(.2f)), new Vector3(1.18f, .04f, .1f), Trim), c.painted);
            Use(Box("Espresso rear glass", c.root, new Vector3(0, c.Y(.96f), c.R(.17f)), new Vector3(1.06f, .29f, .02f), Glass), c.glazed);
            c.Doors(c.F(-.8f), c.R(.3f));
            c.Mirrors(c.F(-.82f));
            c.Gun(1.16f, c.R(.2f));
            c.Plate(.4f); c.Exhaust(1, .22f);
        }

        // 1988 Chevrolet K1500: a full-size regular cab with a long bed, a flat hood and a chrome face.
        static void Copperado(Car c)
        {
            c.Body(1f, .46f, .5f, .02f);
            c.shell.archSides = 4;
            c.shell.tubWall = c.painted;
            c.S(c.F(.78f), 1.03f, 1.09f, .86f, half: .96f, low: .58f);
            c.S(c.F(.7f), 1.1f, 1.13f, .9f);
            c.S(c.F(-.5f), 1.14f, 1.17f, .92f, Screen, Window);
            c.S(c.F(-.9f), 1.15f, 1.82f, .84f, Rooftop, Window);
            c.S(c.F(-1.96f), 1.15f, 1.83f, .84f, Screen, Flank, true);
            c.S(c.F(-2f), 1.15f, 1.44f, .86f);
            c.S(c.F(-2.04f), 1.14f, 1.15f, .93f, Open, drop: 0, floor: .82f);
            c.S(c.R(-1.02f), 1.14f, 1.15f, .93f, Open, drop: 0, floor: .82f);
            c.Wheels(.4f, .26f, c.chrome, .58f, gap: .1f);
            c.Bumper(true, c.chrome, .6f, .78f, .09f, .2f);
            c.Bumper(false, c.chrome, .6f, .76f, .08f, .18f);
            c.Grille("Copperado chrome grille", .92f, 1.02f, .22f, Patterned(CarTextures.Eggcrate, Chromium, new Vector2(10, 3), .88f, 1));
            c.Lamps(.72f, .92f, .34f, .2f, false, .02f);
            c.Signals(.72f, .8f, .34f, .04f);
            c.Tails(.9f, .98f, .12f, .26f);
            c.Pair("Copperado tailgate band", 0, .98f, c.TailZ - .008f, new Vector3(1.5f, .1f, .03f), c.chrome);
            c.shell.Strake(c.chrome, c.F(-.6f), c.R(.6f), .5f, .04f);
            c.Doors(c.F(-.52f), c.F(-1.94f), c.F(-2.06f));
            c.Mirrors(c.F(-.62f), false);
            c.Handles(1.04f, c.F(-1.7f));
            c.Gun(1.83f, c.F(-1.44f));
            c.Plate(.68f); c.Exhaust(1, .5f);
        }

        // The Atlas Scarface: an economy hatch found without its front end and rebuilt for the desert. Ahead of the
        // doors the body is only as wide as the engine bay, and the front wheels stand clear of it on knobbly tyres
        // behind a tube bumper. The muffler is on the hood, there is a rack on the roof, and a small orange safety
        // pennant flies four car-heights up on a whip wound with rope light.
        static void Scarface(Car c)
        {
            // Flat tan with red showing through in streaks: a pattern on the paint, laid along the body.
            Material body = c.enemy ? c.painted : Patterned(CarTextures.Streaks, Color.white, new Vector2(.42f, .6f), .2f);
            Material steel = Reflective(new Color(.085f, .088f, .095f), SunGlint.Metal, .35f, .6f, 1.4f);
            Material frontMount = Material(new Color(.075f, .077f, .079f), 0, .12f);
            frontMount.name = "Scarface front mount matte";
            Material rusty = Reflective(new Color(.46f, .36f, .28f), SunGlint.Metal, .4f, .85f, 1.7f);
            Material mesh = Patterned(CarTextures.Eggcrate, new Color(.17f, .17f, .18f), new Vector2(11, 5), .3f);
            c.Body(.78f, .3f, .5f, .04f);
            c.shell.paint = c.shell.lower = c.shell.roof = c.shell.frame = body;
            c.S(c.F(.5f), .64f, .72f, .44f, half: .5f, low: .42f);
            c.S(c.F(.42f), .72f, .78f, .47f, half: .52f, low: .36f);
            c.S(c.F(-.36f), .9f, .96f, .5f, half: .54f);
            // The body steps out to its full width at the doors: the step is the bare hinge pillar the fender used to cover.
            c.S(c.F(-.4f), .93f, .975f, .65f, Screen, Window);
            c.S(c.F(-1.02f), .94f, 1.45f, .55f, Rooftop, Window);
            c.S(-.36f, .94f, 1.465f, .56f, Rooftop, Window, true);
            c.S(c.R(-.06f), .95f, 1.435f, .55f, Screen, Flank, true);
            c.S(c.R(-.5f), .91f, .97f, .62f);
            c.S(c.R(-.6f), .85f, .91f, .56f, half: .7f, low: .4f);
            c.Wheels(.29f, .18f, steel, .6f, frontRadius: .335f, frontWidth: .23f, openFront: true);
            float nose = c.NoseZ;

            // With the fenders gone the front suspension shows: a strut and its spring up to the tower, and the lower arm.
            for (int side = -1; side <= 1; side += 2)
            {
                float hub = side * (c.FrontWheelX - .13f), tower = side * .5f;
                Beam("Scarface strut", c.root, new Vector3(hub, c.Y(.34f), c.front), new Vector3(tower, c.Y(.8f), c.front - .03f), .028f, Trim);
                Use(Beam("Scarface coil spring", c.root, new Vector3(Mathf.Lerp(hub, tower, .4f), c.Y(.52f), c.front - .012f), new Vector3(Mathf.Lerp(hub, tower, .95f), c.Y(.78f), c.front - .028f), .06f, Trim), rusty);
                Beam("Scarface control arm", c.root, new Vector3(hub, c.Y(.24f), c.front), new Vector3(side * .3f, c.Y(.36f), c.front - .16f), .024f, Trim);
            }

            // The rebuilt face: lamps and a radiator on the bare core support, behind a tube bumper with a mesh plate.
            c.Lamps(.36f, .65f, .2f, .2f, true, .035f);
            c.Pair("Turn signal", .47f, .52f, nose, new Vector3(.12f, .1f, .12f), null, .5f, Amber);
            c.Grille("Scarface radiator", .6f, .3f, .2f, mesh);
            float bar = nose + .2f;
            void Tube(Vector3 a, Vector3 b) => Use(Beam("Scarface tube bumper", c.root, a, b, .028f, Trim), frontMount);
            Tube(new Vector3(-.62f, c.Y(.5f), bar), new Vector3(.62f, c.Y(.5f), bar));
            // The hoop and its mesh stand in the middle; the lamps look out either side of them.
            Tube(new Vector3(-.22f, c.Y(.8f), bar + .02f), new Vector3(.22f, c.Y(.8f), bar + .02f));
            for (int side = -1; side <= 1; side += 2)
            {
                Tube(new Vector3(side * .62f, c.Y(.5f), bar), new Vector3(side * .5f, c.Y(.47f), nose - .12f));
                Tube(new Vector3(side * .25f, c.Y(.5f), bar), new Vector3(side * .22f, c.Y(.8f), bar + .02f));
            }
            // This is the front plate area. Keep its back and surround matte: the reflective strip directly
            // behind it caught the low sun and strobed against the nearby headlamps as the camera moved.
            Use(Box("Scarface front plate backing", c.root, new Vector3(0, c.Y(.65f), bar - .015f),
                new Vector3(.52f, .34f, .018f), Trim), frontMount);
            Use(Box("Scarface bumper mesh", c.root, new Vector3(0, c.Y(.65f), bar + .01f), new Vector3(.44f, .3f, .014f), Trim), mesh);
            Box("Scarface tow hook", c.root, new Vector3(.36f, c.Y(.4f), nose + .12f), new Vector3(.07f, .12f, .18f), Orange);
            c.OnTopPair("Scarface hood pin", .3f, c.F(.36f), c.F(.3f), .05f, .012f, c.chrome, .002f);

            // The muffler stands on the hood with a rain flap on its stack. Nitro leaves from the top of it.
            float stackX = .3f, stackZ = c.F(.2f), hood = c.shell.CrownAt(stackZ);
            Use(Cylinder("Scarface hood muffler", c.root, new Vector3(stackX, hood + .19f, stackZ), .07f, .38f, Trim), rusty);
            Use(Cylinder("Scarface exhaust stack", c.root, new Vector3(stackX, hood + .49f, stackZ), .034f, .24f, Trim), rusty);
            Use(Cylinder("Scarface rain flap", c.root, new Vector3(stackX, hood + .64f, stackZ - .02f), .05f, .012f, Trim), rusty).transform.localRotation = Quaternion.Euler(-38, 0, 0);
            Beam("Scarface muffler stay", c.root, new Vector3(stackX, hood + .3f, stackZ), new Vector3(stackX - .2f, hood + .01f, stackZ - .22f), .008f, Trim);
            Group(NitroExhaust.ExitName, c.root, new Vector3(stackX, hood + .62f, stackZ)).localRotation = Quaternion.Euler(-90, 0, 0);
            c.Exhaust(0, 0);

            // The roof rack: a tube basket with a wind deflector under stickers, the spare, two ammo cans and a spot
            // lamp. The turret stands in the front of it.
            float deck = 1.52f, rail = 1.66f, rackFront = c.F(-1.06f), rackRear = c.R(-.04f);
            const float rackHalf = .5f;
            void Rail(Vector3 a, Vector3 b) => Use(Beam("Scarface roof rack", c.root, a, b, .017f, Trim), steel);
            for (int side = -1; side <= 1; side += 2)
            {
                Rail(new Vector3(side * rackHalf, c.Y(deck), rackFront), new Vector3(side * rackHalf, c.Y(deck), rackRear));
                Rail(new Vector3(side * rackHalf, c.Y(rail), rackFront), new Vector3(side * rackHalf, c.Y(rail), rackRear));
                for (int post = 0; post < 4; post++)
                {
                    float z = Mathf.Lerp(rackFront, rackRear, post / 3f);
                    Rail(new Vector3(side * rackHalf, c.Y(deck - .09f), z), new Vector3(side * rackHalf, c.Y(rail), z));
                }
            }
            for (int rung = 0; rung < 5; rung++)
            {
                float z = Mathf.Lerp(rackFront, rackRear, rung / 4f);
                Rail(new Vector3(-rackHalf, c.Y(deck), z), new Vector3(rackHalf, c.Y(deck), z));
                if (rung == 0 || rung == 4) Rail(new Vector3(-rackHalf, c.Y(rail), z), new Vector3(rackHalf, c.Y(rail), z));
            }
            Use(Box("Scarface sticker deflector", c.root, new Vector3(0, c.Y(1.6f), rackFront + .06f), new Vector3(1.02f, .2f, .012f), Cream),
                Patterned(CarTextures.Stickers, Color.white, Vector2.one, .45f)).transform.localRotation = Quaternion.Euler(-32, 0, 0);
            Cylinder("Scarface rack spare", c.root, new Vector3(0, c.Y(deck + .11f), rackRear + .32f), .28f, .19f, Tyre);
            Use(Cylinder("Scarface rack spare rim", c.root, new Vector3(0, c.Y(deck + .11f), rackRear + .32f), .17f, .2f, Trim), steel);
            c.Pair("Scarface ammo can", .385f, deck + .11f, rackRear + .34f, new Vector3(.17f, .19f, .3f), null, 0, new Color(.29f, .32f, .2f));
            Cylinder("Scarface spot lamp", c.root, new Vector3(-.43f, c.Y(rail + .09f), rackFront + .01f), .06f, .1f, Trim).transform.localRotation = Quaternion.Euler(90, 0, 0);
            Shape("Rack lamp lens", c.root, PrimitiveType.Cylinder, new Vector3(-.43f, c.Y(rail + .09f), rackFront + .064f), new Vector3(.1f, .006f, .1f), new Color(1, .93f, .78f), false, 0, .8f, 1.6f)
                .transform.localRotation = Quaternion.Euler(90, 0, 0);
            c.Gun(deck + .01f, rackFront - .37f);

            // Suzuki in profile on each door, facing forward, and the number on the rear quarters.
            c.shell.Decal(Patterned(CarTextures.DogProfile, Color.white, Vector2.one, .2f, 0, .5f), .44f, .04f, .34f, .92f);
            for (int side = -1; side <= 1; side += 2)
                Text("G10-993", c.root, new Vector3(side * (c.shell.HalfAt(c.R(.36f)) + .004f), c.Y(.86f), c.R(.36f)), .085f, Trim, Quaternion.Euler(0, -side * 90, 0));

            c.Tails(.5f, .72f, .28f, .13f);
            c.Bumper(false, steel, .44f, .58f, .05f, .2f);
            c.Doors(c.F(-.42f), -.38f);
            c.Mirrors(c.F(-.52f), false);
            c.Handles(.84f, -.2f);
            c.Plate(.52f, .16f);
            Use(Box("Scarface rear plate backing", c.root, new Vector3(0, c.Y(.52f), c.TailZ - .148f),
                new Vector3(.44f, .17f, .014f), Trim), Material(new Color(.055f, .06f, .055f), 0, .08f));

            // The whip at the back left corner, its pennant four car-heights up: a little orange triangle, there to be seen.
            WhipFlag.Build(c.root, new Vector3(-.7f, c.Y(.95f), c.R(-.34f)), 4 * 1.465f - .95f, new Color(1f, .4f, .04f), new Color(1f, .8f, .46f));
        }

        // 1970 Dodge Challenger R/T: a long hood, a short deck, wide hips, a black mouth with four round lamps.
        static void Contender(Car c)
        {
            c.Body(.93f, .21f, .55f, .02f);
            c.S(c.F(.82f), .58f, .64f, .78f, half: .88f, low: .3f);
            c.S(c.F(.72f), .66f, .7f, .82f);
            c.S(c.F(-.86f), .85f, .88f, .8f, Screen, Window, half: .91f);
            c.S(c.F(-1.34f), .86f, 1.29f, .64f, Rooftop, Window, half: .9f);
            c.S(-.4f, .88f, 1.3f, .65f, Rooftop, Window, half: .91f);
            c.S(c.R(.52f), .92f, 1.28f, .64f, Screen, Flank, true, half: .94f);
            c.S(c.R(-.1f), .93f, .96f, .78f, half: .94f);
            c.S(c.R(-.84f), .9f, .93f, .8f);
            c.S(c.R(-.94f), .84f, .88f, .76f, half: .88f, low: .32f);
            c.Wheels(.345f, .24f, c.chrome, .6f);
            c.Bumper(true, c.chrome, .34f, .44f, .07f, .24f);
            c.Bumper(false, c.chrome, .4f, .5f, .07f, .24f);
            c.Grille("Contender grille mouth", .54f, 1.56f, .14f, Patterned(CarTextures.Eggcrate, new Color(.1f, .1f, .11f), new Vector2(18, 2), .3f), .004f);
            for (int side = -1; side <= 1; side += 2)
                for (int lamp = 0; lamp < 2; lamp++)
                    Shape("Headlight", c.root, PrimitiveType.Cylinder, new Vector3(side * (.44f + lamp * .2f), c.Y(.54f), c.NoseZ + .02f), new Vector3(.13f, .02f, .13f),
                        VehicleLights.HeadlightAmber, false, 0, .8f, 2.6f).transform.localRotation = Quaternion.Euler(90, 0, 0);
            c.Pair("Contender tail panel", 0, .7f, c.TailZ - .006f, new Vector3(1.6f, .13f, .03f), c.black);
            c.Pair("Tail light", .44f, .7f, c.TailZ - .014f, new Vector3(.6f, .08f, .04f), null, 1.4f, TailRed);
            // The R/T hood: twin scoops on a blacked-out centre.
            Material satin = Material(new Color(.03f, .032f, .035f), 0, .22f);
            c.OnTop("Contender hood blackout", 0, c.F(.56f), c.F(-.8f), .78f, .006f, satin, .001f);
            c.OnTopPair("Contender hood scoop", .2f, c.F(.1f), c.F(-.4f), .22f, .05f, satin, .006f);
            c.shell.Strake(c.black, c.F(-.9f), c.R(-.6f), .86f, .04f, .005f);
            c.Doors(c.F(-.88f), -.44f);
            c.Mirrors(c.F(-.96f), false);
            c.Handles(.8f, -.3f);
            c.Gun(1.3f, -.42f);
            c.Plate(.6f); c.Exhaust(2, .24f);
        }
    }

    /// <summary>Small patterns made once in code and worn as albedo maps (see <see cref="WorldArt.Patterned"/>).</summary>
    static class CarTextures
    {
        static Texture2D zebra, slats, bars, eggcrate;

        /// <summary>Zebra print for the Heat's seats: wavering black stripes of uneven weight on off-white.</summary>
        public static Texture2D Zebra => zebra ? zebra : zebra = Make("Zebra print", 128, (u, v) =>
        {
            float sway = .07f * Mathf.Sin(u * 6.2832f + 1.3f) + .04f * Mathf.Sin(u * 12.566f + v * 6.2832f + .5f) + .025f * Mathf.Sin(u * 25.13f - v * 12.566f);
            float band = Mathf.Sin((v + sway) * 6.2832f * 5);
            // Stripes thicken and pinch out along their length.
            float weight = .1f + .55f * Mathf.Sin(u * 6.2832f * 1.5f + v * 6.2832f * 2) * Mathf.Sin(u * 9.42f + 2);
            float ink = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(weight - .12f, weight + .12f, band));
            return Color.Lerp(new Color(.93f, .9f, .82f), new Color(.035f, .035f, .04f), ink);
        });
        /// <summary>Horizontal slats with dark gaps: grilles and engine-cover louvres.</summary>
        public static Texture2D Slats => slats ? slats : slats = Make("Slats", 32, (u, v) => Shade(Mathf.Repeat(v, 1) < .62f ? 1 : .12f));
        /// <summary>Upright bars with dark gaps.</summary>
        public static Texture2D Bars => bars ? bars : bars = Make("Bars", 32, (u, v) => Shade(Mathf.Repeat(u, 1) < .55f ? 1 : .1f));
        /// <summary>Eggcrate: bright bars round dark cells.</summary>
        public static Texture2D Eggcrate => eggcrate ? eggcrate : eggcrate = Make("Eggcrate", 32, (u, v) => Shade(u < .28f || v < .28f ? 1 : .1f));

        static Texture2D streaks, stickers, dogProfile;

        /// <summary>
        /// The Scarface's paint: flat desert tan, with the red of an earlier coat showing through in long streaks and a
        /// little mottling. Tiles in both directions; the streaks run along u.
        /// </summary>
        public static Texture2D Streaks => streaks ? streaks : streaks = Make("Streaked tan paint", 256, (u, v) =>
        {
            Color tan = new Color(.77f, .67f, .5f), red = new Color(.62f, .27f, .24f);
            float mottle = Tiled(u * 9, v * 9, 9, 9, 3) - .5f;
            // Long thin streaks, gathered into a few patches rather than spread evenly.
            float streak = Edge(.52f, .72f, Tiled(u * 3, v * 16, 3, 16, 11)) * Edge(.3f, .56f, Tiled(u * 2, v * 3, 2, 3, 5));
            Color paint = Color.Lerp(tan * (1 + mottle * .16f), red, streak * .7f);
            paint.a = 1;
            return paint;
        });
        /// <summary>A panel plastered with stickers: bright slabs and ovals overlapping on black.</summary>
        public static Texture2D Stickers => stickers ? stickers : stickers = StickerSheet();
        /// <summary>Suzuki's head in profile, black, facing right, with everything else cut away (see WorldArt.Patterned's cutout).</summary>
        public static Texture2D DogProfile => dogProfile ? dogProfile : dogProfile = Make("Dog profile", 128, (u, v) =>
        {
            // Neck point, back of the neck and head, two pricked ears, brow, muzzle, jaw and chest.
            Vector2[] outline = { new Vector2(.36f, .02f), new Vector2(.14f, .36f), new Vector2(.1f, .6f), new Vector2(.16f, .74f), new Vector2(.2f, .99f),
                new Vector2(.36f, .78f), new Vector2(.46f, .97f), new Vector2(.56f, .74f), new Vector2(.66f, .66f), new Vector2(.74f, .6f), new Vector2(.95f, .5f),
                new Vector2(.99f, .43f), new Vector2(.94f, .38f), new Vector2(.82f, .33f), new Vector2(.66f, .28f), new Vector2(.56f, .2f), new Vector2(.52f, .08f) };
            float cover = 0;
            for (int sy = 0; sy < 3; sy++)
                for (int sx = 0; sx < 3; sx++)
                {
                    Vector2 p = new Vector2(u + (sx - 1) / 384f, v + (sy - 1) / 384f);
                    // The eye is left open so the paint shows through it.
                    bool eye = new Vector2((p.x - .63f) / .042f, (p.y - .575f) / .024f).sqrMagnitude < 1;
                    if (Inside(outline, p) && !eye) cover += 1 / 9f;
                }
            return new Color(.03f, .03f, .035f, cover);
        }, true);

        static Texture2D StickerSheet()
        {
            // Laid down in a fixed order from a fixed seed: the same sheet every time.
            var random = new System.Random(993);
            Color[] inks = { new Color(.93f, .16f, .2f), new Color(.98f, .8f, .1f), new Color(.1f, .45f, .85f), Color.white, new Color(.1f, .68f, .4f),
                new Color(1f, .5f, .1f), new Color(.82f, .2f, .62f), new Color(.1f, .1f, .12f), new Color(.5f, .85f, .95f) };
            const int count = 90;
            var centre = new Vector2[count]; var size = new Vector2[count]; var ink = new Color[count]; var second = new Color[count]; var oval = new bool[count];
            for (int i = 0; i < count; i++)
            {
                centre[i] = new Vector2((float)random.NextDouble(), (float)random.NextDouble());
                size[i] = new Vector2(.035f + (float)random.NextDouble() * .06f, .09f + (float)random.NextDouble() * .2f);
                ink[i] = inks[random.Next(inks.Length)]; second[i] = inks[random.Next(inks.Length)]; oval[i] = random.Next(3) == 0;
            }
            return Make("Sticker sheet", 256, (u, v) =>
            {
                Color colour = new Color(.05f, .05f, .06f, 1);
                for (int i = 0; i < count; i++)
                {
                    Vector2 d = new Vector2((u - centre[i].x) / size[i].x, (v - centre[i].y) / size[i].y);
                    if (oval[i] ? d.sqrMagnitude > 1 : Mathf.Abs(d.x) > 1 || Mathf.Abs(d.y) > 1) continue;
                    // A band of a second colour across each one stands in for its lettering.
                    colour = Mathf.Abs(d.y) < .3f && Mathf.Abs(d.x) < .78f ? second[i] : ink[i];
                }
                return colour;
            }, true);
        }

        /// <summary>Whether a point lies inside a closed outline (crossing count).</summary>
        static bool Inside(Vector2[] outline, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
                if (outline[i].y > p.y != outline[j].y > p.y && p.x < (outline[j].x - outline[i].x) * (p.y - outline[i].y) / (outline[j].y - outline[i].y) + outline[i].x) inside = !inside;
            return inside;
        }
        /// <summary>Smooth noise that repeats every <paramref name="periodX"/> by <paramref name="periodY"/> cells, so a texture made of it tiles.</summary>
        static float Tiled(float x, float y, int periodX, int periodY, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int ax = (x0 % periodX + periodX) % periodX, ay = (y0 % periodY + periodY) % periodY, bx = (ax + 1) % periodX, by = (ay + 1) % periodY;
            return Mathf.Lerp(Mathf.Lerp(SunGlint.Hash(ax, ay, seed), SunGlint.Hash(bx, ay, seed), fx), Mathf.Lerp(SunGlint.Hash(ax, by, seed), SunGlint.Hash(bx, by, seed), fx), fy);
        }

        /// <summary>0 below <paramref name="from"/>, 1 above <paramref name="to"/>, eased between (Mathf.SmoothStep blends two values instead).</summary>
        static float Edge(float from, float to, float value) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(from, to, value));
        static Color Shade(float value) => new Color(value, value, value, 1);
        static Texture2D Make(string name, int size, System.Func<float, float, Color> pixel, bool clamp = false)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) pixels[y * size + x] = pixel((x + .5f) / size, (y + .5f) / size);
            texture.SetPixels(pixels); texture.Apply(true, true);
            return texture;
        }
    }
}
