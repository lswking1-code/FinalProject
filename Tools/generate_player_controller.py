import os

clips = [
    ("Idle", "cd0e85178e1fb0446bf2aa35733827e7"),
    ("LookUp1", "986b4d6ba3e54054abc1c37e9e3f4138"),
    ("LookUp2", "cc7142b8aea575749ab082814a6a01a4"),
    ("Run1", "3de19bd696e809f4393917211c67ebef"),
    ("Run2", "136980e8eb6566a4d815edcf50374265"),
    ("LookUpRun1", "701b1444d60754f4a8ef036f2ec9810a"),
    ("LookUpRun2", "8057a83588399f34c80d23cfac13efef"),
    ("LookUpRun3", "cac5439cd7a61b04d99903cc08f95b8b"),
    ("Crouch1", "7bd7b610fb8e0334d94202c625676572"),
    ("Crouch2", "7c1f89f2c31b71f4cbd00f13c9b74e14"),
    ("CrouchMove1", "d9e24c200aabc8e41880100ae758eea2"),
    ("CrouchMove2", "62267cc10be36d348991d1fda101e633"),
    ("Jump", "243b1dee79d86e54ab499b93c297efaa"),
    ("Jump2", "a704ffd794e74d043ad7c778621884fb"),
    ("Land1", "c2e22b81bb6284d4481bf5b8acc35451"),
    ("Stop1", "db701e7a83dcebf41925721621ce018f"),
    ("Stop2", "6f3efb2cf648cb94d9404c44781081cc"),
]

lines = ["%YAML 1.1", "%TAG !u! tag:unity3d.com,2011:"]
state_ids = {}

for i, (name, guid) in enumerate(clips):
    sid = 9101000 + i
    state_ids[name] = sid
    y = (i % 6) * 80
    x = (i // 6) * 250
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
        "  m_Transitions: []",
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
    "--- !u!1107 &9100500",
    "AnimatorStateMachine:",
    "  serializedVersion: 6",
    "  m_ObjectHideFlags: 1",
    "  m_CorrespondingSourceObject: {fileID: 0}",
    "  m_PrefabInstance: {fileID: 0}",
    "  m_PrefabAsset: {fileID: 0}",
    "  m_Name: Base Layer",
    "  m_ChildStates:",
])

for name, _ in clips:
    sid = state_ids[name]
    lines.extend([
        "  - serializedVersion: 1",
        f"    m_State: {{fileID: {sid}}}",
        "    m_Position: {x: 0, y: 0, z: 0}",
    ])

lines.extend([
    "  m_ChildStateMachines: []",
    "  m_AnyStateTransitions: []",
    "  m_EntryTransitions: []",
    "  m_StateMachineTransitions: {}",
    "  m_StateMachineBehaviours: []",
    "  m_AnyStatePosition: {x: 50, y: 20, z: 0}",
    "  m_EntryPosition: {x: 50, y: 120, z: 0}",
    "  m_ExitPosition: {x: 800, y: 120, z: 0}",
    "  m_ParentStateMachinePosition: {x: 800, y: 20, z: 0}",
    f"  m_DefaultState: {{fileID: {state_ids['Idle']}}}",
    "--- !u!91 &9100000",
    "AnimatorController:",
    "  m_ObjectHideFlags: 0",
    "  m_CorrespondingSourceObject: {fileID: 0}",
    "  m_PrefabInstance: {fileID: 0}",
    "  m_PrefabAsset: {fileID: 0}",
    "  m_Name: Player",
    "  serializedVersion: 5",
    "  m_AnimatorParameters: []",
    "  m_AnimatorLayers:",
    "  - serializedVersion: 5",
    "    m_Name: Base Layer",
    "    m_StateMachine: {fileID: 9100500}",
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

path = os.path.join(os.path.dirname(__file__), "..", "Assets", "Arts", "Metal Slug", "Player.controller")
path = os.path.normpath(path)
with open(path, "w", encoding="utf-8") as f:
    f.write("\n".join(lines) + "\n")

print("Wrote", path)
