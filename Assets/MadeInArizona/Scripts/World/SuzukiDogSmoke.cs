namespace MadeInArizona
{
    /// <summary>Small hook for smoke runners without exposing Suzuki's authored route.</summary>
    public static class SuzukiDogSmoke
    {
        public static bool GarageRoamingReady(SuzukiDog dog,out string issue)
        {
            if(!dog){issue="Suzuki is missing";return false;}
            if(dog.IsRiding){issue="garage Suzuki was created in riding mode";return false;}
            if(!dog.HasRoamingRoute){issue="garage Suzuki has no roaming route";return false;}
            if(dog.DistanceFromHome>22f){issue="Suzuki left the safe garage floor";return false;}
            issue=null;return true;
        }

        public static bool HasReachedFloorAwayFromBed(SuzukiDog dog)
            =>dog&&dog.DistanceFromHome>.8f&&dog.LocalFloorHeight<.08f;
    }
}
