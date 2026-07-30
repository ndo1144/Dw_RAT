using System;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ItemData
{
    public int id;                  // 인덱스 (100, 101...)
    public string type;             // 타입 (무기, 방어구...)
    public string name;             // 이름
    public string itemName;         // UIEffectManager 호환용 이름 (name과 동일값)
    public string grade;            // 등급 (일반, 희귀...)
    public string description;      // 아이템 설명
    public float effectValue;       // 실제 계산에 사용할 수치
    public int price;               // [v14.10] 상점 가격 (자동 산출)
    public UnityEngine.Sprite icon; // 아이템 아이콘
    public int upgradeTargetIndex = -1; // 강화 후 변환될 아이템 ID (-1이면 강화 불가)
    public ItemType itemType; 
    public enum ItemType { Normal, Elite, Active }
}

public class EconomyManager : MonoBehaviour
{
    public static EconomyManager Instance { get; private set; }
    [Tooltip("비워두면 Resources/ItemData1.csv 자동 로드")]
    public TextAsset itemCsvFile;
    private List<ItemData> itemDatabase = new List<ItemData>();
    public static event Action<int> OnGoldChanged;
    public static event Action<List<int>> OnInventoryChanged;
    [Header("Player Inventory")]
    public List<int> ownedItemIDs = new List<int>(); // 현재 보유한 아이템 인덱스들
    [SerializeField]
    private int currentGold;
    public int CurrentGold => currentGold;

    void Awake()
    {
        // [헌법 §28] 인스턴스 우선 할당
        if (Instance == null) Instance = this;

        // [중요] 리스트가 인스펙터에서 null일 수 있으므로 강제 할당
        if (ownedItemIDs == null) ownedItemIDs = new List<int>();

        // [헌법 §40/§41] 씬 전환 후 GameFlowManager 영구 데이터로부터 인벤토리 및 골드 복구
        if (GameFlowManager.Instance?.currentRunData != null)
        {
            ownedItemIDs = new List<int>(GameFlowManager.Instance.currentRunData.ownedItemIndices);
            currentGold = GameFlowManager.Instance.currentRunData.gold; // [추가] 골드 영구 보존 동기화
        }

        // CSV 자동 로드: 인스펙터 미할당 시 Resources/ItemData1.csv 사용
        if (itemCsvFile == null)
        {
            itemCsvFile = Resources.Load<TextAsset>("ItemData2");
            if (itemCsvFile == null)
                Debug.LogError("[Economy] ItemData2.csv 로드 실패! Assets/Resources/ItemData2.csv 가 존재하는지 확인하세요.");
        }

        if (itemCsvFile != null) LoadItemCSV();
    }
    void Update()
    {
        // 숫자 6 키를 눌렀을 때 (Alpha6는 키보드 상단 숫자 6입니다)
        if (Input.GetKeyDown(KeyCode.Alpha6))
        {
            AddGold(100);
            Debug.Log("<color=yellow>[Cheat]</color> 6번 키 입력: 100골드를 획득했습니다!");
        }
    }
    private void OnDestroy()
    {
        // [헌법 §20] 인스턴스 해제하여 메모리 누수 및 잘못된 참조 방지
        if (Instance == this) Instance = null;
    }

    // CSV에서 기본 정보(인덱스, 타입, 이름, 등급)와 수치를 로드
    void LoadItemCSV()
    {
        itemDatabase.Clear();
        string[] rows = itemCsvFile.text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        for (int i = 1; i < rows.Length; i++)
        {
            string[] cols = rows[i].Split(',');
            // 0:ID, 1:Type, 2:Name, 3:Description, 4:Grade... 순서 준수
            if (cols.Length >= 4)
            {
                if (int.TryParse(cols[0].Trim(), out int idValue))
                {
                    ItemData item = new ItemData();
                    item.id = idValue;
                    item.type = cols[1].Trim();
                    item.name = cols[2].Trim();
                    item.itemName = item.name; // UIEffectManager_C 호환
                    item.description = cols[3].Trim();

                    // value 파싱 로직 (% 기호 제거 및 수치 변환)
                    string rawValue = cols[4].Replace("%", "").Trim();
                    float.TryParse(rawValue, out item.effectValue);

                    item.grade = cols.Length > 5 ? cols[5].Trim() : "Normal";

                    // [v14.7.8] 강화 대상 자동 할당: 100번대(일반) 아이템은 +200번(300번대)을 강화 대상으로 지정
                    // CSV 파일 수정을 최소화하기 위해 코드에서 규칙 기반 매핑을 기본으로 수행합니다.
                    item.upgradeTargetIndex = -1;
                    if (idValue >= 100 && idValue < 300)
                        item.upgradeTargetIndex = idValue + 200;

                    // 만약 CSV에 명시적으로 7번째 컬럼(index 6)이 있다면 해당 값을 최우선으로 사용
                    if (cols.Length > 6 && int.TryParse(cols[6].Trim(), out int upgId))
                        item.upgradeTargetIndex = upgId;

                    // [v14.7.7] icon 자동 로드: Resources/Icons 폴더에서 ID를 기반으로 로드
                    item.icon = Resources.Load<Sprite>($"Icons/{item.id}");

                    // 강화 아이콘 폴백: 300번대 아이콘이 없으면 대응하는 100번대(id-200) 로드
                    if (item.icon == null && item.id >= 300 && item.id < 400)
                    {
                        item.icon = Resources.Load<Sprite>($"Icons/" + (item.id - 200));
                    }

                    if (item.icon == null)
                    {
                        Debug.LogWarning($"[Economy] 아이템 {item.id} ({item.name})의 아이콘 로드 실패 (Icons/{item.id})");
                    }
                    // [v14.10] 가격 자동 산출: 사용(800+)=10~15, 특별(700+)=40~50, 일반=25~35
                    if (idValue >= 800)
                        item.price = UnityEngine.Random.Range(10, 16);
                    else if (idValue >= 700)
                        item.price = UnityEngine.Random.Range(40, 51);
                    else
                        item.price = UnityEngine.Random.Range(25, 36);

                    itemDatabase.Add(item);
                }
            }
        }
        Debug.Log($"<color=green>[Economy]</color> {itemDatabase.Count}개의 아이템 정보 로드 완료.");
    }
    // [API] 특정 아이템의 전체 정보를 전달하는 함수
    // [전달용] 특정 아이템의 정보를 이름이나 타입으로 찾기
    public ItemData GetItemInfo(int id)
    {
        return itemDatabase.Find(i => i.id == id);
    }
    // [API] 현재 인벤토리에 특정 타입의 아이템이 있는지 확인 (예: "무기")
    public bool HasItemType(string typeName)
    {
        foreach (int id in ownedItemIDs)
        {
            ItemData data = GetItemInfo(id);
            if (data != null && data.type == typeName) return true;
        }
        return false;
    }
    public bool HasItem(int id)
    {
        // 보유 중인 리스트(ownedItemIDs)에 해당 번호가 있는지 확인
        return ownedItemIDs.Contains(id);
    }
    // EconomyManager.cs 내부에 추가
    public void ShowInventoryLog()
    {
        Debug.Log($"<color=white>━━━━━━━━━━ [내 가방 확인 (I)] ━━━━━━━━━━</color>");

        if (ownedItemIDs.Count == 0)
        {
            Debug.Log("<color=gray>현재 비어있습니다.</color>");
        }
        else
        {
            foreach (int id in ownedItemIDs)
            {
                ItemData data = GetItemInfo(id);
                if (data != null)
                {
                    Debug.Log($"<color=cyan>▶ [{data.name}]</color> : {data.description} (ID: {id})");
                }
            }
        }
        Debug.Log($"<color=white>━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━</color>");
    }
    // EconomyManager.cs 내부에 추가
    public void UseItem(int itemId)
    {
        // [헌법 §12 v11.1] 803번 자유 드래그 중에는 다른 아이템 사용 불가
        if (Match3Manager.Instance != null && Match3Manager.Instance.isFreeDragActive)
        {
            Debug.LogWarning("<color=orange>[경고]</color> 자유 드래그 중에는 아이템을 사용할 수 없습니다.");
            return;
        }
        if (!ownedItemIDs.Contains(itemId)) return;

        ItemData data = GetItemInfo(itemId);

        // [v14.2 추가] 회복 아이템(802) 사용 시 체력 가득 참 여부 체크
        if (itemId == 802)
        {
            if (CombatManager.Instance != null && CombatManager.Instance.playerHP >= CombatManager.Instance.maxHP)
            {
                Debug.LogWarning($"<color=orange>[경고]</color> 체력이 이미 가득 차있어 {data?.name}을(를) 사용할 수 없습니다.");
                return; 
            }
        }

        // 1. 인벤토리에서 선 소모 처리 (헌법 v11.1 원칙)
        if (itemId >= 800)
        {
            ownedItemIDs.Remove(itemId);
            SyncRemoveToRunData(itemId);
            OnInventoryChanged?.Invoke(ownedItemIDs);
        }

        // 2. 효과 적용
        bool isEffectApplied = false;
        if (CombatManager.Instance != null)
        {
            AudioDirector.Instance.PlayItemUse();
            switch (itemId)
            {
                case 701:
                    if (Match3Manager.Instance != null)
                    {
                        int bombQty = (data != null && data.effectValue > 0) ? (int)data.effectValue : 1;
                        Match3Manager.Instance.ActivateBombRat(bombQty);
                    }
                    isEffectApplied = true;
                    break;
                case 801: 
                    CombatManager.Instance.ExecuteItem801(); 
                    isEffectApplied = true; 
                    break;
                case 802:
                    float healRatio = (data != null) ? data.effectValue / 100f : 0.3f;
                    CombatManager.Instance.ApplyHeal(CombatManager.Instance.maxHP * healRatio);
                    isEffectApplied = true;
                    break;
                case 803: 
                    if (Match3Manager.Instance != null) Match3Manager.Instance.ExecuteItem803(); 
                    isEffectApplied = true; 
                    break;
                case 804: 
                    CombatManager.Instance.ExecuteItem804(); 
                    isEffectApplied = true; 
                    break;
                case 805: 
                    CombatManager.Instance.ExecuteItem805(); 
                    isEffectApplied = true; 
                    break;
            }
        }

        if (isEffectApplied)
        {
            Debug.Log($"<color=red>[소모]</color> {data?.name} 아이템을 사용했습니다.");
        }
        else
        {
            // 효과 적용 실패 시 복구 로직 (옵션) 또는 로그
            Debug.LogWarning($"<color=yellow>[알림]</color> {data?.name} 효과가 적용되지 않았습니다.");
        }
    }

    // ────────────────────────────────────────────────────────
    // [헌법 §40] GameFlowManager.currentRunData 동기화 헬퍼
    // 모든 아이템 변경은 반드시 이 헬퍼를 통해 RunData에도 반영한다.
    // ────────────────────────────────────────────────────────
    private void SyncAddToRunData(int itemId)
    {
        var runData = GameFlowManager.Instance?.currentRunData;
        if (runData != null && !runData.ownedItemIndices.Contains(itemId))
        {
            runData.ownedItemIndices.Add(itemId);
            
            // [v14.40] 특별 아이템(701, 702, 703) 최초 획득 시 1회성 기믹 예약
            if (itemId == 701 || itemId == 702 || itemId == 703)
            {
                runData.isFiveMatchGimmickPending = true;
                Debug.Log($"<color=yellow>[Economy]</color> 특별 아이템({itemId}) 획득! 다음 전투에 5매치 기믹이 예약되었습니다.");
            }
        }
    }

    private void SyncRemoveToRunData(int itemId)
    {
        GameFlowManager.Instance?.currentRunData?.ownedItemIndices.Remove(itemId);
    }

    // [API] 아이템 추가 (UIEffectManager_C.AddItemToInventoryImmediate 에서 호출)
    public void AddItem(int itemId)
    {
        if (ownedItemIDs.Contains(itemId))
        {
            Debug.LogWarning($"[Economy] 아이템 {itemId}는 이미 보유 중입니다.");
            return;
        }
        // 1. 로컬 리스트 추가
        ownedItemIDs.Add(itemId);
        // 2. [헌법 §40] GameFlowManager 영구 저장 데이터 동기화
        SyncAddToRunData(itemId);
        // 3. 이벤트 발송 → UIEffectManager_C.RefreshInventoryUI 자동 반영
        OnInventoryChanged?.Invoke(ownedItemIDs);
        Debug.Log($"<color=cyan>[Economy]</color> 아이템 추가: {itemId}. 현재 보유: {ownedItemIDs.Count}개");
    }

    // [API] 아이템 삭제 (CombatManager 등 외부에서 호출)
    public void RemoveItemFromInventory(int itemId)
    {
        if (ownedItemIDs.Remove(itemId))
        {
            // [헌법 §40] GameFlowManager 영구 저장 데이터 동기화
            SyncRemoveToRunData(itemId);
            OnInventoryChanged?.Invoke(ownedItemIDs);
        }
    }
    public void AddGold(int amount)
    {
        if (Instance == null) Instance = this;
        if (amount <= 0) return;

        currentGold += amount;
        
        // [헌법 §33/§40] 로직-저장소 분리 및 데이터 영속성 보장: 재화 변동 시 GameFlowManager 동기화
        if (GameFlowManager.Instance != null && GameFlowManager.Instance.currentRunData != null)
            GameFlowManager.Instance.currentRunData.gold = currentGold;

        Debug.Log($"<color=yellow>[Economy]</color> 골드 획득: {amount}. 현재 골드: {currentGold}");
        // [연결 지점: UIEffectManager_C]
        // 화면 중앙에 "+50 Gold" 같은 플로팅 텍스트 연출 요청
        // OnGoldChanged 이벤트를 통해 UI 텍스트 업데이트
        // UI 및 타 시스템에 알림
        OnGoldChanged?.Invoke(currentGold);
    }

    public bool SpendGold(int amount)
    {
        if (currentGold >= amount)
        {
            currentGold -= amount;

            // [헌법 §33/§40] 로직-저장소 분리 및 데이터 영속성 보장: 재화 변동 시 GameFlowManager 동기화
            if (GameFlowManager.Instance != null && GameFlowManager.Instance.currentRunData != null)
                GameFlowManager.Instance.currentRunData.gold = currentGold;

            Debug.Log($"<color=orange>[Economy]</color> 골드 소비: {amount}. 남은 골드: {currentGold}");
            OnGoldChanged?.Invoke(currentGold);
            return true;
        }

        Debug.LogWarning("골드가 부족합니다.");
        return false;
    }

    // [헌법 §3 API] 무작위 보상 아이템 ID를 큐(리스트) 형태에서 방출 (임시 랜덤 제공)
    public List<int> DequeueGeneralRewards(int count)
    {
        List<int> rewards = new List<int>();

        // 1. 전체 대상 아이템 리스트 추출 (ID 101~299)
        var allNormalItems = itemDatabase.FindAll(i => i.id >= 101 && i.id < 300);

        // 2. 현재 플레이어가 이미 소유한 아이템 ID 목록 가져오기 [헌법 §36/§40]
        var ownedItems = GameFlowManager.Instance.currentRunData.ownedItemIndices;

        // 3. 소유하지 않은 아이템만 필터링 (중복 제거 핵심)
        var availableItems = allNormalItems.FindAll(i =>
        {
            // A. 현재 이 아이템을 이미 가지고 있는가?
            bool alreadyHasOriginal = ownedItems.Contains(i.id);

            // B. 이 아이템의 강화 버전(ID + 200)을 이미 가지고 있는가?
            bool alreadyHasUpgraded = ownedItems.Contains(i.id + 200);

            // 둘 다 없어야만 보상 후보가 될 수 있음
            return !alreadyHasOriginal && !alreadyHasUpgraded;
        });

        // 4. 가용 아이템이 부족할 경우 예외 처리
        if (availableItems.Count == 0)
        {
            Debug.LogWarning("더 이상 획득할 수 있는 새로운 아이템이 없습니다.");
            return rewards;
        }

        // 5. 중복 없이 요청된 개수만큼 추출
        int actualCount = Mathf.Min(count, availableItems.Count);
        for (int i = 0; i < actualCount; i++)
        {
            // [헌법 §39] UnityEngine.Random 명시
            int rndIndex = UnityEngine.Random.Range(0, availableItems.Count);
            int selectedId = availableItems[rndIndex].id;

            rewards.Add(selectedId);

            // 보상 목록 내에서도 중복 방지를 위해 선택된 것은 즉시 제거
            availableItems.RemoveAt(rndIndex);
        }

        return rewards;
    }
    public List<int> DequeueEliteRewards(int count)
    {
        List<int> rewards = new List<int>();

        // 1. 엘리트 대상 아이템 리스트 추출 (ID 701~799)
        // [수정] 엘리트 아이템은 강화가 없으므로 독립적인 유물 풀로 취급합니다.
        var allEliteItems = itemDatabase.FindAll(i => i.id >= 701 && i.id < 800);

        // 2. 현재 플레이어가 이미 소유한 아이템 ID 목록 가져오기
        var ownedItems = GameFlowManager.Instance.currentRunData.ownedItemIndices;

        // 3. 중복 소유 방지 필터링
        var availableItems = allEliteItems.FindAll(i =>
        {
            return !ownedItems.Contains(i.id);
        });

        // 4. 가용 아이템 부족 시 처리
        if (availableItems.Count == 0)
        {
            Debug.LogWarning("[Economy] 모든 엘리트 유물을 획득하여 더 이상 추출할 수 없습니다.");
            return rewards;
        }

        // 5. 중복 없이 추출
        int actualCount = Mathf.Min(count, availableItems.Count);
        for (int i = 0; i < actualCount; i++)
        {
            int rndIndex = UnityEngine.Random.Range(0, availableItems.Count);
            rewards.Add(availableItems[rndIndex].id);

            // 현재 보상 선택지 내 중복 방지
            availableItems.RemoveAt(rndIndex);
        }

        return rewards;
    }
    public List<int> DequeueActiveRewards(int count)
    {
        List<int> rewards = new List<int>();

        // 1. 대상 아이템 리스트 추출 (ID 801~899)
        var allActiveItems = itemDatabase.FindAll(i => i.id >= 801 && i.id < 900);

        // 2. 사용 아이템의 정책 결정 (중복 허용 여부)
        // 만약 사용 아이템을 "종류별로 하나씩만" 소유하게 하고 싶다면 아래 필터를 유지하세요.
        // 만약 "포션 2개"처럼 중복 소유가 가능하다면 이 필터 단계를 건너뛰면 됩니다.
        var ownedItems = GameFlowManager.Instance.currentRunData.ownedItemIndices;
        var availableItems = allActiveItems.FindAll(i =>
        {
            bool alreadyHas = ownedItems.Contains(i.id);
            // 사용 아이템도 강화 버전이 존재한다면 아래 주석 해제
            // bool alreadyHasUpgraded = ownedItems.Contains(i.id + 200); 
            return !alreadyHas;
        });

        if (availableItems.Count == 0)
        {
            Debug.LogWarning("[Economy] 더 이상 추출할 수 있는 액티브 아이템이 없습니다.");
            return rewards;
        }

        // 3. 랜덤 추출
        int actualCount = Mathf.Min(count, availableItems.Count);
        for (int i = 0; i < actualCount; i++)
        {
            int rndIndex = UnityEngine.Random.Range(0, availableItems.Count);
            rewards.Add(availableItems[rndIndex].id);

            // 한 번의 추출 리스트 안에서 중복을 방지하려면 제거
            availableItems.RemoveAt(rndIndex);
        }

        return rewards;
    }
    // EconomyManager.cs 내부에 추가할 상점 전용 로직
    public List<int> GetShopItemList()
    {
        List<int> shopItems = new List<int>();
        var ownedItems = GameFlowManager.Instance.currentRunData.ownedItemIndices;
        // [보완] 현재 이미 가지고 있는 아이템(ownedItemIDs)은 상점 풀에서 제외
        List<ItemData> passivePool = itemDatabase.FindAll(i =>
        i.id >= 101 && i.id < 300 &&
        !ownedItems.Contains(i.id) &&
        !ownedItems.Contains(i.id + 200));

        List<ItemData> activePool = itemDatabase.FindAll(i => i.id >= 801 && i.id < 900);

        shopItems.AddRange(GetRandomUniqueIds(passivePool, 3));
        shopItems.AddRange(GetRandomUniqueIds(activePool, 3));

        return shopItems;
    }

    private List<int> GetRandomUniqueIds(List<ItemData> pool, int count)
    {
        List<int> result = new List<int>();
        if (pool.Count == 0) return result;

        // 풀 복사본 생성 (중복 제거용)
        List<ItemData> tempPool = new List<ItemData>(pool);

        for (int i = 0; i < count; i++)
        {
            if (tempPool.Count == 0) break;
            int rndIndex = UnityEngine.Random.Range(0, tempPool.Count);
            result.Add(tempPool[rndIndex].id);
            tempPool.RemoveAt(rndIndex); // 중복 방지
        }
        return result;
    }

    // [API] 상점 구매 처리 (헌법 §40 영속성 보장 준수)
    public bool BuyItem(int itemId, int price)
    {
        if (CurrentGold < price)
        {
            Debug.LogWarning("[Shop] 골드가 부족합니다.");
            return false;
        }

        if (SpendGold(price))
        {
            AddItem(itemId); // 내부에서 SyncAddToRunData 및 이벤트 발송 수행
            AudioDirector.Instance.PlayBuy();
            return true;
        }
        return false;
    }
    public void GiveTreasureReward()
    {
        // 1. 아이템 지급 (ID 100: 쥐혈밴드)
        var item = GetItemInfo(100);
        if (item != null)
        {
            // 인벤토리에 추가 (currentRunData에 반영됨)
            AddItem(100);
            Debug.Log($"<color=yellow>[Economy] 아이템 획득: {item.itemName}</color>");
        }

        // 2. 골드 지급
        AddGold(10);

        // 3. UI 연출 요청 (UIEffectManager에게 알림)
        // 보상 획득 팝업 등을 띄우기 위한 이벤트 호출
        // UIEffectManager.Instance.ShowRewardPopup(item, goldAmount);
    }
    public List<int> GetUpgradableItems()
    {
        List<int> upgradableList = new List<int>();

        foreach (int id in ownedItemIDs)
        {
            ItemData data = GetItemInfo(id);
            if (data == null) continue;

            // [체크 1] ID가 300 미만인가? (100~200번대 일반 아이템인가)
            bool isBaseItem = (id < 300);

            // [체크 2] 강화 후의 ID(id + 200) 데이터가 실제로 존재하는가?
            // upgradeTargetIndex를 일일이 세팅하기 힘들다면 이 로직이 가장 확실합니다.
            ItemData nextTierData = GetItemInfo(id + 200);
            bool canBeUpgraded = (nextTierData != null);

            if (isBaseItem && canBeUpgraded)
            {
                upgradableList.Add(id);
            }
        }

        return upgradableList;
    }
}