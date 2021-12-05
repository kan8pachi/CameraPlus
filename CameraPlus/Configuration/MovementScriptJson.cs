﻿using Newtonsoft.Json;

namespace CameraPlus.Configuration
{
    public class AxizWithFoVElements
    {
        public string x { get; set; }
        public string y { get; set; }
        public string z { get; set; }
        public string FOV { get; set; }
    }

    public class AxisElements
    {
        public string x { get; set; }
        public string y { get; set; }
        public string z { get; set; }
    }
    public class VisibleObject
    {
        public bool avatar { get; set; }
        public bool ui { get; set; }
        public bool wall { get; set; }
        public bool wallFrame { get; set; }
        public bool saber { get; set; }
        //public bool cutParticles { get; set; }
        public bool notes { get; set; }
        public bool debri { get; set; }
    }

    [JsonObject("Movements")]
    public class JSONMovement
    {
        [JsonProperty("StartPos")]
        public AxizWithFoVElements startPos { get; set; }
        [JsonProperty("StartRot")]
        public AxisElements startRot { get; set; }
        [JsonProperty("StartHeadOffset")]
        public AxisElements startHeadOffset { get; set; }
        [JsonProperty("EndPos")]
        public AxizWithFoVElements endPos { get; set; }
        [JsonProperty("EndRot")]
        public AxisElements endRot { get; set; }
        [JsonProperty("EndHeadOffset")]
        public AxisElements endHeadOffset { get; set; }
        [JsonProperty("VisibleObject")]
        public VisibleObject visibleObject { get; set; }

        public string TurnToHead { get; set; }
        public string TurnToHeadHorizontal { get; set; }
        public string Duration { get; set; }
        public string Delay { get; set; }
        public string EaseTransition { get; set; }
    }

    public class MovementScriptJson
    {
        public string ActiveInPauseMenu { get; set; }
        public string TurnToHeadUseCameraSetting { get; set; }
        [JsonProperty("Movements")]
        public JSONMovement[] Jsonmovement { get; set; }
    }
}
