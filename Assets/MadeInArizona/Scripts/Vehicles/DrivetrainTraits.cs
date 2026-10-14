using UnityEngine;

namespace MadeInArizona
{
    /// <summary>
    /// What each drivetrain makes of the ground under it. Rear drive is the road car: it puts its power down best on
    /// asphalt and loses the most once the surface is loose. All-wheel drive is the reverse: at its best on dirt, the
    /// only layout that keeps its pace in sand, and paying for it on the road in weight and drag. Front drive sits
    /// between the two. One figure for each drivetrain and kind of ground says how well they suit; drive, cornering
    /// grip and top speed all follow from it, so the figure the garage shows is the whole of it.
    /// </summary>
    public static class DrivetrainTraits
    {
        /// <summary>The kinds of ground a drivetrain tells apart.</summary>
        public enum Ground { Road, Dirt, Sand }

        /// <summary>A drivetrain's footing on one surface, as multipliers on the car's own figures.</summary>
        public readonly struct Footing
        {
            public readonly float drive, cornering, topSpeed;
            public Footing(float fit) { drive = fit; cornering = Mathf.Lerp(1, fit, CorneringShare); topSpeed = Mathf.Lerp(1, fit, TopSpeedShare); }
        }

        // Rows follow the Drivetrain enum (front, rear, all-wheel); columns follow Ground (road, dirt, sand).
        // Dirt and sand penalties are 80% of their former distance below 1; bonuses stay as they were.
        static readonly float[,] Fits =
        {
            { 1.00f, .976f, .728f },
            { 1.08f, .904f, .664f },
            { .95f, 1.14f, 1.00f },
        };
        // Drive takes the whole figure; cornering grip and top speed take these shares of its distance from even.
        const float CorneringShare = .5f, TopSpeedShare = .35f;

        /// <summary>Paved and oiled surfaces are road; sand and mud are sand; everything else, water included, is dirt.</summary>
        public static Ground GroundOf(SurfaceKind surface)
        {
            switch (surface)
            {
                case SurfaceKind.Asphalt: case SurfaceKind.Oil: return Ground.Road;
                case SurfaceKind.Sand: case SurfaceKind.Mud: return Ground.Sand;
                default: return Ground.Dirt;
            }
        }

        /// <summary>How well a drivetrain suits a kind of ground: 1 is even, more is a bonus, less a penalty.</summary>
        public static float Fit(Drivetrain drivetrain, Ground ground) => Fits[Mathf.Clamp((int)drivetrain, 0, 2), (int)ground];

        public static Footing On(Drivetrain drivetrain, SurfaceKind surface) => new Footing(Fit(drivetrain, GroundOf(surface)));

        /// <summary>The three figures as the garage prints them, such as "ROAD +8%  •  DIRT -12%  •  SAND -42%".</summary>
        public static string Summary(Drivetrain drivetrain) =>
            "ROAD " + Percent(Fit(drivetrain, Ground.Road)) + "  •  DIRT " + Percent(Fit(drivetrain, Ground.Dirt)) + "  •  SAND " + Percent(Fit(drivetrain, Ground.Sand));

        static string Percent(float fit)
        {
            int percent = Mathf.RoundToInt((fit - 1) * 100);
            return (percent < 0 ? "" : "+") + percent + "%";
        }
    }
}
