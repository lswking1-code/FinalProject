import os

CLIPS = {
    "CrouchStart": "2c31fd76edae23f4393c64ba3791baac",
    "Crouch": "9cceb9c7627d1fc4da2b0eb5712655fa",
    "CrouchMove": "6bce89dc22ce5b64589a5a7889ea9d5a",
    "CrouchShoot": "b29ebe37ce2938f4ea7f24b0039e5778",
    "CrouchMelee": "339fdaf039c347f48bb860007c291a71",
    "CrouchThrow": "669c27464b0f98649bcf1c9e12aac185",
}

STATE_IDS = {name: 9102000 + i for i, name in enumerate(CLIPS, 1)}
SM_ID = 9102100
CTRL_ID = 9100000

tid = 9103000


def next_tid():
    global tid
    tid += 1
    return tid


def transition(conditions, dst_state, has_exit_time=False, exit_time=0.9, duration=0.25):
    t_id = next_tid()
    lines = [
        f"--- !u!1101 &{t_id}",
        "AnimatorStateTransition:",
        "  m_ObjectHideFlags: 1",
        "  m_CorrespondingSourceObject: {fileID: 0}",
        "  m_PrefabInstance: {fileID: 0}",
        "  m_PrefabAsset: {fileID: 0}",
        "  m_Name: ",
        "  m_Conditions:",
    ]
    for mode, event in conditions:
        lines.extend([
            f"  - m_ConditionMode: {mode}",
            f"    m_ConditionEvent: {event}",
            "    m_EventTreshold: 0",
        ])
    if not conditions:
        lines.append("  []")
    lines.extend([
        "  m_DstStateMachine: {fileID: 0}",
        f"  m_DstState: {{fileID: {dst_state}}}",
        "  m_Solo: 0",
        "  m_Mute: 0",
        "  m_IsExit: 0",
        "  serializedVersion: 3",
        f"  m_TransitionDuration: {duration}",
        "  m_TransitionOffset: 0",
        f"  m_ExitTime: {exit_time}",
        f"  m_HasExitTime: {1 if has_exit_time else 0}",
        "  m_HasFixedDuration: 1",
        "  m_InterruptionSource: 0",
        "  m_OrderedInterruption: 1",
        "  m_CanTransitionToSelf: 1",
    ])
    return t_id, lines


# Build transitions per state
state_transitions = {name: [] for name in CLIPS}

# CrouchStart -> Crouch
t_id, t_lines = transition([], STATE_IDS["Crouch"], has_exit_time=True, exit_time=0.9)
state_transitions["CrouchStart"].append(t_id)
all_transition_lines = t_lines

# Crouch <-> CrouchMove
t_id, t_lines = transition([(1, "IsRun")], STATE_IDS["CrouchMove"])
state_transitions["Crouch"].append(t_id)
all_transition_lines.extend(t_lines)

t_id, t_lines = transition([(2, "IsRun")], STATE_IDS["Crouch"])
state_transitions["CrouchMove"].append(t_id)
all_transition_lines.extend(t_lines)

# Crouch/CrouchMove -> CrouchShoot
for src in ("Crouch", "CrouchMove"):
    t_id, t_lines = transition([(1, "IsShoot")], STATE_IDS["CrouchShoot"])
    state_transitions[src].append(t_id)
    all_transition_lines.extend(t_lines)

# CrouchShoot -> Crouch
t_id, t_lines = transition([(2, "IsShoot")], STATE_IDS["Crouch"])
state_transitions["CrouchShoot"].append(t_id)
all_transition_lines.extend(t_lines)

# Crouch/CrouchMove -> Melee/Throw
for src in ("Crouch", "CrouchMove"):
    for trigger, dst in (("Melee", "CrouchMelee"), ("Throw", "CrouchThrow")):
        t_id, t_lines = transition([(1, trigger)], STATE_IDS[dst])
        state_transitions[src].append(t_id)
        all_transition_lines.extend(t_lines)

# Action states return to Crouch
for src in ("CrouchMelee", "CrouchThrow"):
    t_id, t_lines = transition([], STATE_IDS["Crouch"], has_exit_time=True, exit_time=0.9)
    state_transitions[src].append(t_id)
    all_transition_lines.extend(t_lines)

positions = {
    "CrouchStart": (300, 50),
    "Crouch": (300, 170),
    "CrouchMove": (500, 170),
    "CrouchShoot": (700, 50),
    "CrouchMelee": (700, 170),
    "CrouchThrow": (700, 290),
}

lines = ["%YAML 1.1", "%TAG !u! tag:unity3d.com,2011:"]
lines.extend(all_transition_lines)

for name, guid in CLIPS.items():
    sid = STATE_IDS[name]
    x, y = positions[name]
    trans_refs = "\n".join(f"  - {{fileID: {t}}}" for t in state_transitions[name])
    lines.extend([
        f"--- !u!1102 &{sid}",
        "AnimatorState:",
        "  serializedVersion: 6",
        "  m_ObjectHideFlags: 1",
        "  m_CorrespondingSourceObject: {fileID: 0}",
        "  m_PrefabInstance: {fileID: 0}",
        "  m_PrefabAsset: {fileID: 0}",
        f"  m_Name: {name}",
        "  m_Speed: 1",
        "  m_CycleOffset: 0",
        "  m_Transitions:",
        trans_refs if trans_refs else "  []",
        "  m_StateMachineBehaviours: []",
        f"  m_Position: {{x: {x}, y: {y}, z: 0}}",
        "  m_IKOnFeet: 0",
        "  m_WriteDefaultValues: 1",
        "  m_Mirror: 0",
        "  m_SpeedParameterActive: 0",
        "  m_MirrorParameterActive: 0",
        "  m_CycleOffsetParameterActive: 0",
        "  m_TimeParameterActive: 0",
        f"  m_Motion: {{fileID: 7400000, guid: {guid}, type: 2}}",
        "  m_Tag: ",
        "  m_SpeedParameter: ",
        "  m_MirrorParameter: ",
        "  m_CycleOffsetParameter: ",
        "  m_TimeParameter: ",
    ])

lines.extend([
    f"--- !u!1107 &{SM_ID}",
    "AnimatorStateMachine:",
    "  serializedVersion: 6",
    "  m_ObjectHideFlags: 1",
    "  m_CorrespondingSourceObject: {fileID: 0}",
    "  m_PrefabInstance: {fileID: 0}",
    "  m_PrefabAsset: {fileID: 0}",
    "  m_Name: Base Layer",
    "  m_ChildStates:",
])
for name in CLIPS:
    x, y = positions[name]
    lines.extend([
        "  - serializedVersion: 1",
        f"    m_State: {{fileID: {STATE_IDS[name]}}}",
        f"    m_Position: {{x: {x}, y: {y}, z: 0}}",
    ])

lines.extend([
    "  m_ChildStateMachines: []",
    "  m_AnyStateTransitions: []",
    "  m_EntryTransitions: []",
    "  m_StateMachineTransitions: {}",
    "  m_StateMachineBehaviours: []",
    "  m_AnyStatePosition: {x: 50, y: 20, z: 0}",
    "  m_EntryPosition: {x: 50, y: 120, z: 0}",
    "  m_ExitPosition: {x: 900, y: 120, z: 0}",
    "  m_ParentStateMachinePosition: {x: 900, y: 20, z: 0}",
    f"  m_DefaultState: {{fileID: {STATE_IDS['CrouchStart']}}}",
    f"--- !u!91 &{CTRL_ID}",
    "AnimatorController:",
    "  m_ObjectHideFlags: 0",
    "  m_CorrespondingSourceObject: {fileID: 0}",
    "  m_PrefabInstance: {fileID: 0}",
    "  m_PrefabAsset: {fileID: 0}",
    "  m_Name: crouch",
    "  serializedVersion: 5",
    "  m_AnimatorParameters:",
])

for pname, ptype in [
    ("IsRun", 4),
    ("IsShoot", 4),
    ("Melee", 9),
    ("Throw", 9),
]:
    lines.extend([
        f"  - m_Name: {pname}",
        f"    m_Type: {ptype}",
        "    m_DefaultFloat: 0",
        "    m_DefaultInt: 0",
        "    m_DefaultBool: 0",
        "    m_Controller: {fileID: 9100000}",
    ])

lines.extend([
    "  m_AnimatorLayers:",
    "  - serializedVersion: 5",
    "    m_Name: Base Layer",
    f"    m_StateMachine: {{fileID: {SM_ID}}}",
    "    m_Mask: {fileID: 0}",
    "    m_Motions: []",
    "    m_Behaviours: []",
    "    m_BlendingMode: 0",
    "    m_SyncedLayerIndex: -1",
    "    m_DefaultWeight: 0",
    "    m_IKPass: 0",
    "    m_SyncedLayerAffectsTiming: 0",
    "    m_Controller: {fileID: 9100000}",
])

out = os.path.normpath(
    os.path.join(os.path.dirname(__file__), "..", "Assets", "Animation", "crouch.controller")
)
with open(out, "w", encoding="utf-8") as f:
    f.write("\n".join(lines) + "\n")

print("Wrote", out)
