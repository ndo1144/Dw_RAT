using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [v14.9] 동적 몬스터 스폰 시스템
/// Battle 씬 진입 시 GameFlowManager의 StageType과 depth에 따라
/// 몬스터를 자동으로 생성하고 배치합니다.
/// 
/// ※ 실행 순서: Awake()에서 스폰 → CombatManager.Start()에서 InitializeMonstersInScene() 인식
/// </summary>
public class MonsterSpawner : MonoBehaviour
{
    
    [System.Serializable]
    public class MonsterPrefabEntry
    {
        public string monsterID;    // "N01", "N02", "E01", "B01"
        public GameObject prefab;   // 인스펙터에서 드래그 할당
    }
    public List<MonsterPrefabEntry> monsterPrefabs = new List<MonsterPrefabEntry>();
    public static MonsterSpawner Instance { get; private set; }

    [Header("스폰 설정")]
    [Tooltip("일반 몬스터용 Y좌표 (실측 바닥 높이 -3.6f 권장)")]
    public float normalSpawnY = -3.2f;

    [Tooltip("엘리트 몬스터용 Y좌표 (실측값 -2.61f 권장)")]
    public float eliteSpawnY = -2.61f;

    [Tooltip("보스 몬스터용 Y좌표")]
    public float bossSpawnY = -2.61f;

    [Tooltip("몬스터 그룹의 X축 기준 오프셋 (양수 = 오른쪽)")]
    public float groupOffsetX = 6.0f;

    [Tooltip("몬스터 간 X축 간격")]
    public float spacing = 1.2f;

    void Awake()
    {
        if (Instance == null) Instance = this;
        SpawnMonsters();
    }

    private void SpawnMonsters()
    {
        // [v14.9.5] 플레이어와 높이를 맞추기 위해 런타임에 플레이어 위치를 감지합니다.
        GameObject player = GameObject.Find("Player");
        if (player == null) player = GameObject.FindGameObjectWithTag("Player");
        
        /* [v14.12.8] 플레이어 높이 자동 감지가 수동 오프셋을 덮어쓰는 문제가 있어 비활성화합니다.
        if (player != null)
        {
            normalSpawnY = player.transform.position.y;
            Debug.Log($"<color=cyan>[MonsterSpawner]</color> 플레이어 높이 감지 완료: Y = {normalSpawnY}");
        }
        */

        // 1. GameFlowManager에서 현재 스테이지 정보 조회
        if (GameFlowManager.Instance == null)
        {
            Debug.LogWarning("<color=yellow>[MonsterSpawner]</color> GameFlowManager 없음. 기본 N01 × 2 스폰.");
            SpawnNormalGroup(new List<string> { "N01", "N01" }, -1f);
            return;
        }

        StageType stageType = GameFlowManager.Instance.currentStageType;
        int depth = GameFlowManager.Instance.CurrentFloor;

        // 2. 스테이지 타입에 따라 스폰할 몬스터 목록 결정
        List<string> spawnList = DecideSpawnList(stageType, depth);
        Debug.Log($"<color=cyan>[MonsterSpawner]</color> StageType: {stageType} | Depth: {depth} | 스폰: {string.Join(", ", spawnList)}");

        // 3. 스폰 수에 따른 공격력 조정
        float atkOverride = -1f;

        if (stageType == StageType.Battle && depth <= 3 && spawnList.Count >= 3)
        {
            atkOverride = 3f;
        }

        // 4. 실제 스폰 - 등급별 메서드 분리 호출 [v14.12.5]
        if (spawnList.Count > 0)
        {
            string firstID = spawnList[0];
            if (firstID.StartsWith("E") || firstID.StartsWith("B"))
            {
                SpawnSpecialMonster(firstID, atkOverride);
            }
            else
            {
                SpawnNormalGroup(spawnList, atkOverride);
            }
        }
    }

    /// <summary>
    /// StageType과 depth에 따라 스폰할 몬스터 ID 리스트를 결정합니다.
    /// </summary>
    private List<string> DecideSpawnList(StageType stageType, int depth)
    {
        List<string> list = new List<string>();

        switch (stageType)
        {
            case StageType.Battle:
                if (depth <= 2)
                {
                    // 초반 (depth 0~2): N01 × 2~3마리
                    int count = UnityEngine.Random.Range(2, 4);
                    for (int i = 0; i < count; i++) list.Add("N01");
                }
                else if (depth <= 6)
                {
                    // 중반 (depth 3~6, 엘리트 전후): N01 1마리 + N02 1마리
                    list.Add("N01");
                    list.Add("N02");
                }
                else
                {
                    // 후반: N01 + N02 합쳐서 3마리
                    int n01Count = UnityEngine.Random.Range(1, 3); // 1 또는 2
                    int n02Count = 3 - n01Count;
                    for (int i = 0; i < n01Count; i++) list.Add("N01");
                    for (int i = 0; i < n02Count; i++) list.Add("N02");
                }
                break;

            case StageType.EliteBattle:
                list.Add("E01");
                break;

            case StageType.Boss:
                list.Add("B01");
                break;

            default:
                list.Add("N01");
                list.Add("N01");
                break;
        }

        return list;
    }

    /// <summary>
    /// [v14.12.5] 일반 몬스터(N01, N02 등) 리스트를 받아 균등 배치합니다.
    /// </summary>
    private void SpawnNormalGroup(List<string> monsterIDs, float atkOverride)
    {
        int count = monsterIDs.Count;
        if (count == 0) return;

        List<Vector3> positions = CalculatePositions(count);

        for (int i = 0; i < count; i++)
        {
            string id = monsterIDs[i];
            GameObject prefab = GetPrefabByID(id);
            if (prefab == null) continue;

            GameObject monsterObj = Instantiate(prefab, positions[i], Quaternion.identity);
            
            // 위치 설정
            Vector3 pos = monsterObj.transform.position;
            // [v14.12.7] 일반 몬스터(N01, N02)는 피벗 차이로 인해 더 낮은 바닥 높이(-3.3f) 적용
            pos.y = normalSpawnY+0.05f; 
            pos.z = -1.0f;
            monsterObj.transform.position = pos;

            // 시선 방향 설정 (사용자 요청: 최종 왼쪽 방향 고정 시도)
            Vector3 curScale = prefab.transform.localScale;
            curScale.x = -Mathf.Abs(curScale.x); 
            monsterObj.transform.localScale = curScale;

            SetupMonsterCommon(monsterObj, id, i, atkOverride);
            Debug.Log($"<color=green>[SpawnNormal]</color> {id} 스폰 완료: {pos}");
        }
    }

    /// <summary>
    /// [v14.12.5] 엘리트(E01) 또는 보스(B01)를 단일 소환합니다.
    /// </summary>
    private void SpawnSpecialMonster(string id, float atkOverride)
    {
        GameObject prefab = GetPrefabByID(id);
        if (prefab == null) return;

        // [v14.12.6] 보스(B01) 또한 -2.61f 바닥 높이 적용
        float finalY = id.StartsWith("E") ? eliteSpawnY : bossSpawnY;
        Vector3 spawnPos = new Vector3(groupOffsetX+0.2f, finalY, -1.0f);

        GameObject monsterObj = Instantiate(prefab, spawnPos, Quaternion.identity);

        // 시선 방향 설정 (사용자 요청: 최종 왼쪽 방향 고정 시도)
        Vector3 curScale = prefab.transform.localScale;
        curScale.x = Mathf.Abs(curScale.x); 
        monsterObj.transform.localScale = curScale;

        SetupMonsterCommon(monsterObj, id, 0, atkOverride);
        Debug.Log($"<color=yellow>[SpawnSpecial]</color> {id} 스폰 완료: {spawnPos}");
    }

    /// <summary>
    /// [v14.12.5] 몬스터 등급과 무관한 공통 초기화 루틴
    /// </summary>
    private void SetupMonsterCommon(GameObject monsterObj, string id, int index, float atkOverride)
    {
        // 레이어 설정
        SpriteRenderer[] srs = monsterObj.GetComponentsInChildren<SpriteRenderer>();
        foreach (var sr in srs)
        {
            sr.sortingLayerName = "Default";
            sr.sortingOrder = 500;
        }

        monsterObj.name = $"Monster_{id}_{index}";

        // 물리 설정 (자식 객체의 Rigidbody2D까지 모두 비활성화하여 낙하 방지 [v14.12.10])
        Rigidbody2D[] rbs = monsterObj.GetComponentsInChildren<Rigidbody2D>();
        if (rbs.Length > 0)
        {
            foreach (var rb in rbs)
            {
                rb.gravityScale = 0f;
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.linearVelocity = Vector2.zero; // 혹시 모를 초기 속도 제거
            }
        }

        // 클릭 컴포넌트 설정
        MonsterClick mc = monsterObj.GetComponent<MonsterClick>() ?? monsterObj.AddComponent<MonsterClick>();
        mc.monsterIDString = id;
        mc.atkOverride = atkOverride;

        // 콜라이더 설정
        if (monsterObj.GetComponent<Collider2D>() == null)
        {
            BoxCollider2D col = monsterObj.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(3.0f, 4.0f);
        }
    }

    /// <summary>
    /// 몬스터 수에 따라 균등 배치 좌표를 계산합니다.
    /// </summary>
    private List<Vector3> CalculatePositions(int count)
    {
        List<Vector3> positions = new List<Vector3>();

        if (count == 1)
        {
            // [v14.9.2] 에디터 실측치에 따라 Z를 -1.0f로 설정
            positions.Add(new Vector3(groupOffsetX, normalSpawnY, -1.0f));
        }
        else
        {
            // 중앙 정렬 + 그룹 오프셋 적용
            float totalWidth = (count - 1) * spacing;
            float startX = groupOffsetX - totalWidth / 2f;

            for (int i = 0; i < count; i++)
            {
                positions.Add(new Vector3(startX + i * spacing, normalSpawnY, 0f));
            }
        }

        return positions;
    }

    /// <summary>
    /// 몬스터 ID로 매핑된 프리팹을 찾습니다.
    /// </summary>
    public GameObject GetPrefabByID(string id)
    {
        foreach (var entry in monsterPrefabs)
        {
            if (entry.monsterID == id) return entry.prefab;
        }
        return null;
    }
}
