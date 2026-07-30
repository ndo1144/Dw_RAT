using Assets.PixelFantasy.PixelMonsters.Common.Scripts;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems; // 상단에 추가
/*
 OnMoveCompleted: 퍼즐 블록이 터질 때마다 호출되어 공격/방어 수치를 쌓습니다.
1회용 방어막: MonsterTurnCoroutine 내부에서 몬스터가 공격할 때마다 
currentShield를 소모시키며, 부족한 만큼만 playerHP가 깎이도록 설계되었습니다.
이벤트 시스템: Action을 사용하여 CombatManager가 UI나 게임 흐름 제어 
스크립트를 직접 참조하지 않고도 정보를 전달할 수 있게 했습니다.
 */

public partial class CombatManager : MonoBehaviour
{
    // 어디서든 CombatManager.Instance로 접근 가능하게 합니다.
    public static CombatManager Instance { get; private set; }
    [System.Serializable]
    public class MonsterInstance
    {
        public string id;
        public MonsterGrade type;
        public string name;
        public float hp;
        public float maxHp;
        public float atk;
        public GameObject obj; // [수정] 몬스터 오브젝트 참조 필드 추가
        public bool isStunned = false; // [804] 기절 상태 플래그
        // [보스 기믹 추가]
        public int bossPhase = 1;
        public int bossChargeCount = 0;
        public int removedContaminationTotal = 0;
        public bool isSummonedMinion = false; // [v14.5.9] 소환된 고기방패 여부
        public int summonCooldownCounter = 0; // [v14.6.9] 엘리트 소환 주기 관리 (초기값 0)
        
        public MonsterInstance() { }
        // 인수를 4개 받는 생성자 추가 (v12.9 대응)
        public MonsterInstance(string name, float maxHp, MonsterGrade grade, GameObject obj)
        {
            this.name = name;
            this.maxHp = maxHp;
            this.hp = maxHp; // 초기 체력은 최대 체력과 동일하게 설정
            this.type = grade;
            this.obj = obj;
            this.isStunned = false;
        }
        // MonsterData를 기반으로 인스턴스를 생성하는 생성자
        public MonsterInstance(MonsterData data, GameObject gameObject = null)
        {
            this.id = data.id;
            this.name = data.name;

            // CSV 범위 기반 개별 랜덤 체력 부여
            float randomHp = (data.minHp != data.maxHp) ? 
                UnityEngine.Random.Range((int)data.minHp, (int)data.maxHp + 1) : data.hp;

            this.hp = randomHp;
            this.maxHp = randomHp;
            this.atk = data.atk;
            this.obj = gameObject;
        }
    }
    // [외부 매니저 연동용 이벤트 (Action)]
    // 몬스터 체력이 변할 때 UI(UIEffectManager_C) 등에 알림
    public static Action<int, float> OnMonsterHealthChanged;
    public static Action<int, Vector3, bool> OnMonsterDamagePopupRequested;
    // [v14.14] 플레이어 피격 시 직접 팝업 요청 (방어막 흡수 여부 무관)
    public static Action<int> OnPlayerDamagePopupRequested;
    // [v14.53] 플레이어 방어막 피격 시 팝업 요청
    public static Action<int> OnPlayerShieldDamagePopupRequested;
    // 플레이어 체력이나 방어막이 변할 때 UI에 알림
    public static Action<float, float> OnPlayerStatusChanged;
    // 전투가 종료되었을 때 승리(true)/패배(false) 여부를 GameFlowManager 등에 알림
    public static Action<bool> OnCombatFinished;
    // [헌법 §3] 상태 갱신 및 보상 이벤트 (보상 선택 UI 요청용)
    public static event Action<List<int>, int> OnShowRewardPicker;
    public static event Action<string, Vector3> OnEffectTrigger;
    // [v14.37] 몬스터 차징 상태 변경 이벤트 (오브젝트, 현재값, 최대값)
    public static Action<GameObject, int, int> OnMonsterChargeChanged; // [v14.37.4] GameObject 기반 동기화
    public static Action<GameObject, float> OnMonsterAtkChanged; // [v14.46] 공격력 표시용 이벤트 추가
    // CombatManager.cs (상태 관리 파트)
    public bool isPlayerTurn { get; private set; } = true;
    public TextMeshProUGUI moveText; // 인스펙터에서 할당 필요
    public bool isWaitingForUI = false; // [신규] UI 선택창 활성화 여부 플래그
    [Header("Player Stats")]
    // 플레이어 현재 체력
    public float playerHP = 50f;
    public float maxHP = 50f;
    // 플레이어가 보유한 현재 방어막 수치
    public float currentShield = 0f;
    public TextMeshProUGUI playercurshield;
    //
    [Header("UI Reference")]
    // 2회 이동 후 나타나는 공격/방어 선택창 패널
    public GameObject choiceUIPanel;
    // 첫 번째 선택지의 공격/방어 텍스트
    public TextMeshProUGUI option1TextAt, option1TextDf;
    // 두 번째 선택지의 공격/방어 텍스트
    public TextMeshProUGUI option2TextAt, option2TextDf;
    public float currentAttackBuff = 0f; // [109] 나쥐 효과 누적용
    public float tempAttackBuff = 0f; // 801번 등 1턴 한정 버프 저장소
    public int currentRatKnightStack = 0; // 기사단 스택 필드 추가
    private bool wasKnightActiveLastTurn = false; // 생존 여부 체크용
    private float _resolvedDamage = 0f;
    public bool is801Active = false; // [801] 화염의 물약 활성화 여부
    private bool is804Active = false; // [804] 이번 턴 기절 아이템 사용 여부
    private bool item110Reserved = false;
    private bool is110EffectUsedInThisBattle = false; // 이번 전투에서 110번 효과 사용 여부
    public void AddRatKnightStack(int amount) { currentRatKnightStack += amount; }
    [Header("Combat State")]
    // 현재 씬에 존재하는 몬스터 인스턴스들의 리스트
    public List<MonsterInstance> activeMonsters = new List<MonsterInstance>();
    // 현재 턴에서 수행한 퍼즐 이동 횟수 (최대 2)
    public int moveCount = 0;
    // 플레이어가 공격 대상으로 클릭한 몬스터
    public MonsterInstance selectedTarget;
    // 유저가 선택창에서 최종 결정한 능력치 세트
    private TurnResult finalSelectedResult;
    // 몬스터 등급 정의 (기존 열거형이 있다면 업데이트 필요)
    public enum MonsterGrade { Normal1, Normal2, Elite, Boss }
    // 전투 중 사망한 몬스터 등급 저장용
    private List<MonsterGrade> defeatedMonsterGrades = new List<MonsterGrade>();
    // 각 이동(매칭)마다 계산된 결과값 저장소
    private List<TurnResult> savedResults = new List<TurnResult>();
    //
    public MonsterLoader monsterLoader; // 인스펙터에서 연결
    //인벤토리/아이템/골드
    public EconomyManager economyManager; // 인스펙터에서 연결
    // [몬스터 개별 데이터 구조]
    
    // [2] 몬스터 데이터로부터 인스턴스를 생성하는 시점 (MonsterLoader 연동)
    public void CreateMonsterInstance(MonsterData data, GameObject monsterObj)
    {
        MonsterInstance newMonster = new MonsterInstance(data, monsterObj);

        // 로더의 데이터 테이블에 정의된 grade 문자열을 enum으로 변환하여 할당 (대소문자 무시)
        if (Enum.TryParse(data.type, true, out MonsterGrade assignedGrade))
        {
            newMonster.type = assignedGrade;
        }
        else
        {
            newMonster.type = MonsterGrade.Normal1; // 예외 처리용 기본값
        }

        Debug.Log($"[Monster Init] Created {newMonster.name} with Grade: {newMonster.type} (Original: {data.type})");

        activeMonsters.Add(newMonster);
        if (UIEffectManager_C.Instance != null)
        {
            UIEffectManager_C.Instance.CreateMonsterHPBar(newMonster);
        }
    }
    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }
   
    void Start()
    {
        // [v14.7.9] GameFlowManager로부터 영구 데이터 로드 (HP, MaxHP, Shield)
        if (GameFlowManager.Instance != null && GameFlowManager.Instance.currentRunData != null)
        {
            var data = GameFlowManager.Instance.currentRunData;
            maxHP = data.maxHp;
            playerHP = data.hp;
            currentShield = data.currentShield;
            Debug.Log($"<color=cyan>[Combat Init]</color> HP: {playerHP}/{maxHP}, Shield: {currentShield}");
        }

        // 시작 시 선택창은 비활성화
        if (choiceUIPanel != null) choiceUIPanel.SetActive(false);
        // [수정] 게임 시작 시 초기 방어도 반영
        UpdateShieldText(currentShield);
        UpdateStatus(); // 초기 UI 동기화

        // 씬 시작 시 배치된 몬스터 정보 로드
        InitializeMonstersInScene();

        // [v14.7.6] Item 100/300 (쥐혈밴드) 스테이지 진입 시 자동 발동 (패시브)
        int bandageId = GetActualID(100);
        if (bandageId != -1)
        {
            if (playerHP < maxHP)
            {
                ItemData data = EconomyManager.Instance.GetItemInfo(bandageId);
                float healAmount = (data != null) ? data.effectValue : 2f;
                ApplyHeal(healAmount);
                Debug.Log($"<color=lime>[Passive {bandageId}]</color> 스테이지 진입 효과 발동: {healAmount} 회복 완료.");
            }
        }

        // 초기 상태(체력/방어막 0)를 UI에 동기화
        UpdateStatus();
        // [수정] 시작하자마자 첫 턴 아이템 현황 보고
        StartPlayerTurn();
    }
    // --- [핵심: 데미지 및 방어 수치 계산 로직] ---

    public float CalculateFinalDamage(float baseDmg, int spcCount = 0, int maxMatch = 0)
    {
        float finalDmg = baseDmg+ spcCount;
        if (economyManager == null) return finalDmg;

        // [101] 억센 발톱 (+301): 공격력 고정 증가
        int clawId = GetActualID(101);
        if (clawId != -1 && baseDmg > 0)
        {
            float bonus = economyManager.GetItemInfo(clawId).effectValue;
            finalDmg += bonus;
        }

        // [103] 공방일체 (+303): 스페셜 블록 매치 시 공격력 보너스
        int comboId = GetActualID(103);
        if (comboId != -1 && spcCount >= 3)
        {
            float bonus = economyManager.GetItemInfo(comboId).effectValue;
            finalDmg += bonus;
        }

        // [106] 스네이크 (+306): 데미지 무작위 변동
        int snakeId = GetActualID(106);
        if (snakeId != -1 && (baseDmg > 0 || spcCount > 0))
        {
            float range = economyManager.GetItemInfo(snakeId).effectValue;
            int randomVariance = UnityEngine.Random.Range(-(int)range - 1, (int)range + 1);
            finalDmg += randomVariance;

            string color = randomVariance >= 0 ? "orange" : "red";
            Debug.Log($"<color={color}>[Passive {snakeId}]</color> 데미지 {randomVariance} 변동 (최종: {finalDmg})");
        }
        // [109] 나쥐 효과: 현재까지 쌓인 버프를 공격력에 합산 (여기서 ++ 하지 않음)
        finalDmg += currentAttackBuff;
        // [110] 빅게임 헌터 (+310): 4매치 이상 발생 시 보너스
        int hunterId = GetActualID(110);
        if (hunterId != -1 && maxMatch >= 4)
        {
            float bonusRate = 0.1f * (maxMatch - 3); 
            finalDmg += (baseDmg * bonusRate);
            Debug.Log($"<color=yellow>[Passive {hunterId}]</color> {maxMatch}매치 보너스 적용");
        }

        // [111] 역전의 용쥐 (+311): 저체력 시 데미지 상승
        int dragonId = GetActualID(111);
        if (dragonId != -1 && (playerHP / maxHP) <= 0.3f)
        {
            float bonus = economyManager.GetItemInfo(dragonId).effectValue;
            finalDmg += bonus;
        }
        // [703] 쥐돌이 기사단: 5매치 시 기사단 3기 소환
        if (economyManager.ownedItemIDs.Contains(703) && maxMatch >= 5)
        {
            currentRatKnightStack += 3;
            Debug.Log($"<color=cyan>[703 쥐돌이 기사단]</color> 기사단 3기 소환! (현재 스택: {currentRatKnightStack})");
        }
        
        finalDmg += tempAttackBuff;   // [801] 일시적 버프 합산 (추가)
        return Mathf.Max(0, finalDmg);
    }

        public float CalculateFinalDefense(float baseDef, int spcCount = 0, int matchCount = 0)
        {
        float finalDef = baseDef+ spcCount;
        if (economyManager == null) return finalDef;

        // [102] 털보송송이 (+302): 방어도 고정 증가
        int furId = GetActualID(102);
        if (furId != -1 && baseDef > 0)
        {
            float bonus = economyManager.GetItemInfo(furId).effectValue;
            finalDef += bonus;
        }

        // [103] 공방일체 (+303): 스페셜 블록 매치 시 방어력 보너스
        int comboDefId = GetActualID(103);
        if (comboDefId != -1 && spcCount >= 3)
        {
            float bonus = economyManager.GetItemInfo(comboDefId).effectValue;
            finalDef += bonus;
        }
        
        // [703] 쥐돌이 기사단: 5매치 시 두 가지 효과 중 하나 랜덤 발동
        if (economyManager.ownedItemIDs.Contains(703) && matchCount >= 5)
        {
            // 0 또는 1 무작위 선택
            int randomEffect = UnityEngine.Random.Range(0, 2);

            if (randomEffect == 0)
            {
                // 효과 A: 방어 효율 3배 증폭
                finalDef *= 3f;
                Debug.Log("<color=cyan>[703 쥐돌이 기사단]</color> 기사단의 돌격! 방어 효율이 3배로 증가합니다.");
            }
            else
            {
                // 효과 B: 5회 방어급 수치 제공 (고정값 가산 또는 베이스의 대폭 증폭)
                // 여기서는 '완전 방어' 느낌을 위해 베이스 방어력에 큰 고정 보너스를 더하거나 5배를 적용
                finalDef += 50f;
                Debug.Log("<color=cyan>[703 쥐돌이 기사단]</color> 기사단의 방패! 강력한 고정 방어막이 형성됩니다.");
            }
        }return finalDef;
    }
    // [핵심] 109번 버프를 '증가'시키는 함수 (턴 종료 시 1회만 호출할 것)
    public void IncrementTurnBuff()
    {
        int najwiId = GetActualID(109);
        if (najwiId != -1)
        {
            float bonus = economyManager.GetItemInfo(najwiId).effectValue;
            currentAttackBuff += bonus;
            Debug.Log($"<color=yellow>[Passive {najwiId}]</color> 턴 종료 버프 {bonus} 증가 (누적: {currentAttackBuff})");
        }
    }
    // 전투 시작 시 또는 종료 시 버프 초기화 (필요한 곳에서 호출)
    // [805] 턴 되돌리기 아이템: 이번 턴에 사용한 이동(매치) 및 결과를 무효화하고 이동횟수를 0으로 초기화
    public void ExecuteItem805()
    {
        Debug.Log("<color=yellow>[805] 턴 되돌리기 아이템 사용: 현재 턴 데이터를 초기화합니다.</color>");

        // 1. 이동 횟수 초기화
        moveCount = 0;

        // 2. 현재 턴에서 저장된 매치 결과(공격/방어 수치) 전부 삭제
        savedResults.Clear();

        // 3. 임시 공격 버프 초기화 (예: 801 등)
        ResetTurnTemporaryBuffs();

        // 4. UI 상태 초기화: 선택창(choiceUIPanel) 비활성화 및 UI 대기 플래그 해제
        if (choiceUIPanel != null) choiceUIPanel.SetActive(false);
        isWaitingForUI = false;

        // 5. 이동 횟수 UI 텍스트 갱신 (0/2)
        UpdateMoveText();

        // 6. 보드 입력 재활성화 — 선택창이 떠서 막혀있던 터치를 다시 허용
        if (Match3Manager.Instance != null)
        {
            Match3Manager.Instance.SetInteractable(true);
        }

        Debug.Log("<color=lime>[805] 턴 초기화 완료: moveCount=0, savedResults 비우기, 선택창 닫기, 보드 입력 재개.</color>");
    }

    public void ResetBattleBuffs()
    {
        currentAttackBuff = 0f;
    }
    // [702] 보드판 직접 타격: 5매치 시 방어 무시 고정 데미지
    // [702] 보드판 직접 타격: 5매치 시 모든 몬스터에게 방어 무시 고정 데미지 10
    public void TriggerBoardDirectAttack()
    {
        if (economyManager == null || !economyManager.ownedItemIDs.Contains(702)) return;
        if (activeMonsters == null || activeMonsters.Count == 0) return;

        // 3연타 시퀀스 코루틴 시작
        StartCoroutine(TripleBoardStrikeRoutine());
    }

    // [v14.84] 퍼즐보드 폭행 3연타 - 방망이 휘두르기 모션
    public IEnumerator TripleBoardStrikeRoutine()
    {
        if (Match3Manager.Instance == null) yield break;
        Transform boardRoot = Match3Manager.Instance.puzzleParent.parent;
        if (boardRoot == null) yield break;

        Match3Manager.Instance.SetInteractable(false);

        // [v14.88] 방망이 휘두르기: 아래→위→아래
        // 3타: 충전은 길게, 휘두르기는 빠르게!
        float[] damages = { 5f, 5f, 20f };
        bool[] swingDown = { true, false, true };
        float[] chargeTime = { 0.06f, 0.06f, 1.3f }; // 충전(준비 동작) 시간
        float[] swingSpeed = { 0.2f, 0.2f, 0.1f }; // 실제 휘두르기 시간 (3타도 빠르게)
        float[] returnSpeed = { 0.15f, 0.15f, 0.15f };
        float[] swingAngles = { 45f, 45f, 70f };
        int[] blocksToDrop = { 5, 8, 0 };

        for (int i = 0; i < 3; i++)
        {   
            if (AudioDirector.Instance != null && i==2) AudioDirector.Instance.PlayBoardStrike();
            yield return StartCoroutine(BatSwingVisualRoutine(boardRoot, swingDown[i], swingSpeed[i], returnSpeed[i], swingAngles[i], chargeTime[i]));

            // 데미지 적용
            Debug.Log($"<color=red>[702 {i + 1}차 타격]</color> 방망이 휘두르기! 데미지 {damages[i]} 적용!");
            if (AudioDirector.Instance != null && i<2) AudioDirector.Instance.PlayPlayerHit();
            ApplyDirectDamage(damages[i]);

            // [v14.85] 타격 충격으로 블록 일부 떨어뜨리기
            if (Match3Manager.Instance != null && blocksToDrop[i] > 0)
            {
                yield return StartCoroutine(Match3Manager.Instance.ShakeOffBlocksRoutine(blocksToDrop[i]));
            }

            // 다음 타격 전 대기
            if (i == 2) yield return new WaitForSeconds(0.12f);
            else yield return new WaitForSeconds(0.2f);
        }

        CheckAllMonstersDead();
        Match3Manager.Instance.SetInteractable(true);
    }

    // [v14.37] 방어 무시 고정 데미지 적용 헬퍼 (702번 전용)
    private void ApplyDirectDamage(float dmg)
    {
        if (activeMonsters == null || activeMonsters.Count == 0) return;

        // 역순 순회로 리스트 제거 안전성 확보
        for (int i = activeMonsters.Count - 1; i >= 0; i--)
        {
            MonsterInstance target = activeMonsters[i];

            // 엘리트 보호 기믹 (미니언이 대신 맞음)
            if (target.type == MonsterGrade.Elite)
            {
                var minion = activeMonsters.Find(m => m.isSummonedMinion && m.hp > 0);
                if (minion != null)
                {
                    Debug.Log($"<color=orange>[702 방어]</color> {minion.name}이 엘리트 대신 피해를 입습니다.");
                    target = minion;
                }
            }

            // 데미지 적용
            target.hp = Mathf.Max(0, target.hp - dmg);

            // UI 및 연출 알림
            if (target.obj != null)
            {
                OnMonsterDamagePopupRequested?.Invoke(Mathf.RoundToInt(dmg), target.obj.transform.position, dmg >= 30f);
            }
            
            // 인덱스 기반 갱신 (전투 뷰 UI 동기화)
            int index = activeMonsters.IndexOf(target);
            if (index != -1)
            {
                OnMonsterHealthChanged?.Invoke(index, target.hp);
            }

            // 사망 처리
            if (target.hp <= 0)
            {
                Debug.Log($"<color=orange><b>[처치] {target.name} 보드판 폭행으로 처치!</b></color>");
                if (AudioDirector.Instance != null) AudioDirector.Instance.PlayDeath();
                // 몬스터 사망 시 처리는 Constitution Rule 54번 준수
                if (target.obj != null) Destroy(target.obj);
                if (target == selectedTarget) selectedTarget = null;
                activeMonsters.Remove(target);
            }
        }
    }

    // [v14.88] 방망이 휘두르기 - 충전/스윙 분리
    private IEnumerator BatSwingVisualRoutine(Transform board, bool swingDown, float swingDuration, float returnDuration, float baseAngle = 45f, float chargeDuration = -1f)
    {
        Vector3 originalPos = board.position;
        Quaternion originalRot = board.rotation;

        Vector3 pivot = originalPos;
        if (Match3Manager.Instance != null)
        {
            Vector2 sp = Match3Manager.Instance.startPos;
            pivot = new Vector3(sp.x - 1f, sp.y, 0);
        }

        float swingAngle = swingDown ? -baseAngle : baseAngle;
        float windUpAngle = -swingAngle * 0.85f;

        // [v14.88] 충전 시간이 별도로 지정되지 않으면 swingDuration 기반으로 계산
        float actualCharge = (chargeDuration >= 0f) ? chargeDuration : swingDuration * 0.85f;

        float elapsed;

        // 1. 충전 (준비 동작 - 반대 방향으로)
        elapsed = 0f;
        while (elapsed < actualCharge)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / actualCharge);
            float angle = Mathf.Lerp(0f, windUpAngle, t);
            Quaternion rot = Quaternion.Euler(0, 0, angle);
            board.rotation = originalRot * rot;
            board.position = pivot + rot * (originalPos - pivot);
            yield return null;
        }
        // 준비 동작 최종 상태 기록
        Quaternion windUpRotFinal = originalRot * Quaternion.Euler(0, 0, windUpAngle);
        Vector3 windUpPosFinal = pivot + Quaternion.Euler(0, 0, windUpAngle) * (originalPos - pivot);

        // 2. 휘두르기! (가속 커브로 빠르게)
        elapsed = 0f;
        while (elapsed < swingDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / swingDuration);
            float easeT = t * t; // 가속 커브
            float angle = Mathf.Lerp(windUpAngle, swingAngle, easeT);
            Quaternion rot = Quaternion.Euler(0, 0, angle);
            board.rotation = originalRot * rot;
            board.position = pivot + rot * (originalPos - pivot);
            yield return null;
        }
        board.rotation = originalRot * Quaternion.Euler(0, 0, swingAngle);
        board.position = pivot + Quaternion.Euler(0, 0, swingAngle) * (originalPos - pivot);

        // 3. 복귀 (감속 커브)
        elapsed = 0f;
        while (elapsed < returnDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / returnDuration);
            float easeT = 1f - (1f - t) * (1f - t); // 감속 커브
            float angle = Mathf.Lerp(swingAngle, 0f, easeT);
            Quaternion rot = Quaternion.Euler(0, 0, angle);
            board.rotation = originalRot * rot;
            board.position = pivot + rot * (originalPos - pivot);
            yield return null;
        }
        board.position = originalPos;
        board.rotation = originalRot;
    }

    // [v14.37 폐기] 기존 돌진 연출 (호환성 유지용)
    private IEnumerator BoardStrikeVisualRoutine(Transform board, Vector3 direction)
    {
        yield return StartCoroutine(BatSwingVisualRoutine(board, true, 0.08f, 0.2f));
    }

    // [신규] 사용 아이템 발동 함수 (UI 버튼 등에서 호출)
    public void UseActiveItem(int itemId)
    {
        if (economyManager == null || !economyManager.ownedItemIDs.Contains(itemId)) return;

        var itemData = economyManager.GetItemInfo(itemId);
        if (itemData == null) return;

        switch (itemId)
        {
            case 801: // 빠알간 치즈: 1턴간 광역 공격 및 데미지 상승
                ExecuteItem801();
                break;
            case 805: // 턴 되돌리기 아이템
                ExecuteItem805();
                break;
            // 추후 802~804 추가 지점
        }

        // [중요] 사용 아이템은 1회성이므로 사용 후 즉시 제거
        if (itemId >= 801 && itemId <= 805)
        {
            economyManager.ownedItemIDs.Remove(itemId);
            Debug.Log($"<color=red>[소모]</color> {itemData.name}을(를) 사용하여 인벤토리에서 제거되었습니다.");
            // 인벤토리 UI 갱신 이벤트 호출 (필요 시)
        }
    }
    
    // [핵심] 일시적 버프 초기화 메서드 구현
    public void ResetTurnTemporaryBuffs()
    {
        if (tempAttackBuff > 0)
        {
            Debug.Log($"<color=white>턴 종료: 일시적 공격력 버프({tempAttackBuff})가 소멸되었습니다.</color>");
            tempAttackBuff = 0; // 0으로 초기화
        }
        if (is801Active)
        {
            is801Active = false;
        }
    }

    // 플레이어 턴 시작 시 호출 (MonsterTurnCoroutine 종료 지점 혹은 Start 시점)
    public void StartPlayerTurn()
    {

        moveCount = 0;
        savedResults.Clear();
        UpdateMoveText();
        isPlayerTurn = true;
        is804Active = false; // [804] 턴 시작 시 플래그 초기화
        isWaitingForUI = false; // [안전장치] 조작 무한 잠금 방지
        // 디버그 로그로 인스턴스 존재 여부 확인
        if (Match3Manager.Instance != null)
        {
            // [v14.1] 폭탄이 남아있으면 일반 스왑 대신 폭탄 모드로 재개
            if (Match3Manager.Instance.bombCount > 0)
            {
                Match3Manager.Instance.ResumeBombMode();
                Debug.Log($"<color=yellow>[턴 시작]</color> 잔여 폭탄 {Match3Manager.Instance.bombCount}개 감지. 폭탄 선택 모드로 재개.");
            }
            else
            {
                Match3Manager.Instance.ResumePuzzle();
                Debug.Log("<color=cyan>[System] Match3Manager 인스턴스 확인 및 조작 활성화 완료</color>");
            }
        }
        else
        {
            // 만약 이게 뜬다면 하이어라키에 Match3Manager가 없거나 Awake 할당이 안 된 것입니다.
            Debug.LogError("<color=red>[System] Match3Manager.Instance를 찾을 수 없습니다!</color>");
        }
        // [명령 수행] 현재 인벤토리 현황 브리핑
        if (economyManager != null)
        {
            string inventoryBrief = "현재 보유 아이템: ";
            if (economyManager.ownedItemIDs.Count == 0)
            {
                inventoryBrief += "없음";
            }
            else
            {
                List<string> itemStrings = new List<string>();
                foreach (int id in economyManager.ownedItemIDs)
                {
                    var data = economyManager.GetItemInfo(id);
                    // 데이터가 있고, 이름이 "미등록"이 아니며, 실제 문자열이 있을 때만 추가
                    if (data != null && !string.IsNullOrEmpty(data.name) && data.name != "미등록")
                    {
                        // [ID: 이름] - 설명 형식으로 출력
                        string descText = !string.IsNullOrEmpty(data.description) ? data.description : "설명 없음";
                        Debug.Log($"<color=cyan>▶ [{id}: {data.name}]</color> : {descText}");
                    }
                
                }
                inventoryBrief += string.Join(" ", itemStrings);
            }
            Debug.Log($"<color=white>────────── [플레이어 턴 시작] ──────────</color>");
            Debug.Log($"<color=cyan>{inventoryBrief}</color>");
            
        }
    }
    void InitializeMonstersInScene()
    {
        activeMonsters.Clear();
        defeatedMonsterGrades.Clear();

        MonsterClick[] clicks = FindObjectsByType<MonsterClick>(FindObjectsSortMode.None);

        if (clicks.Length == 0)
        {
            Debug.LogWarning("<color=yellow>[Combat]</color> 씬에서 MonsterClick 컴포넌트를 찾지 못했습니다.");
            return;
        }

        foreach (MonsterClick mc in clicks)
        {
            MonsterData data = monsterLoader.GetMonsterByID(mc.monsterIDString);

            if (data != null)
            {
                MonsterInstance newInst = new MonsterInstance(data, mc.gameObject);

                // [v14.9] MonsterSpawner에서 지정한 공격력 오버라이드 적용
                if (mc.atkOverride >= 0f)
                {
                    newInst.atk = mc.atkOverride;
                    Debug.Log($"<color=orange>[ATK Override]</color> {data.name} 공격력: {data.atk} → {mc.atkOverride}");
                }

                // [v14.5.1] 등급 파싱 로직 강화
                MonsterGrade assignedGrade = MonsterGrade.Normal1;

                if (Enum.TryParse(data.type, true, out MonsterGrade parsedGrade))
                {
                    assignedGrade = parsedGrade;
                }
                else if (!string.IsNullOrEmpty(data.id) && data.id.StartsWith("B"))
                {
                    assignedGrade = MonsterGrade.Boss;
                }
                else if (!string.IsNullOrEmpty(data.id) && data.id.StartsWith("E"))
                {
                    assignedGrade = MonsterGrade.Elite;
                }
                else
                {
                    assignedGrade = DetermineGradeFromName(data.name);
                }

                newInst.type = assignedGrade;
                activeMonsters.Add(newInst);
                mc.LinkInstance(this, newInst);
                if (UIEffectManager_C.Instance != null)
                {
                    UIEffectManager_C.Instance.CreateMonsterHPBar(newInst);
                }
                // [v14.7.4] 엘리트 기믹 초기화
                if (newInst.type == MonsterGrade.Elite)
                {
                    newInst.summonCooldownCounter = 2;
                }

                // [보스 기믹] 보스 몬스터라면 시작 시 블록 오염
                if (newInst.type == MonsterGrade.Boss)
                {
                    newInst.bossPhase = 1;
                    if (Match3Manager.Instance != null)
                    {
                        Match3Manager.Instance.bossPhase = 1;
                        Match3Manager.Instance.TriggerRatInvasion(10);
                    }
                }

                Debug.Log($"<color=cyan>[몬스터 등록]</color> {data.name} | HP: {newInst.hp} | ATK: {newInst.atk} | 등급: {newInst.type}");
            }
            else
            {
                Debug.LogError($"[Combat] ID '{mc.monsterIDString}'에 해당하는 몬스터 데이터를 찾을 수 없습니다!");
            }
        }
    }

    /// <summary>
    /// [v14.7.0] 스마트 소환 시스템: 빈 자리를 찾아 방패쥐를 소환하거나 현재 생존자의 체력을 회복시킵니다.
    /// </summary>
    private void SummonEliteMinions(MonsterInstance elite)
    {
        if (elite == null || elite.obj == null) return;

        // 1. 현재 존재하는 방패쥐들 확인 (이름으로 좌/우 슬롯 판별)
        MonsterInstance s1 = activeMonsters.Find(m => m.isSummonedMinion && m.hp > 0 && m.name == "방패쥐 1");
        MonsterInstance s2 = activeMonsters.Find(m => m.isSummonedMinion && m.hp > 0 && m.name == "방패쥐 2");

        // 2. [v14.7.4] 이미 존재하는 녀석들은 체력 회복하지 않음 (유저 요청)
        // if (s1 != null) { s1.hp = s1.maxHp; ... }
        // if (s2 != null) { s2.hp = s2.maxHp; ... }

        // 3. 비어있는 슬롯 소환
        MonsterData minionData = monsterLoader.GetMonsterByID("N01"); 
        if (minionData == null) minionData = monsterLoader.monsterTable.Find(m => m.type.ToLower().Contains("normal"));
        if (minionData == null) return;

        if (s1 == null) CreateSpecificMinion(elite, minionData, 0, -0.8f);
        if (s2 == null) CreateSpecificMinion(elite, minionData, 1, 0.8f);
    }

    private void CreateSpecificMinion(MonsterInstance elite, MonsterData data, int index, float offset)
    {
        // [v14.7.1] 'Missing Script' 경고 방지를 위한 사전 검사
        if (elite == null || elite.obj == null)
        {
            Debug.LogWarning($"<color=red>[경고]</color> 엘리트 객체가 유효하지 않아 {index}번 방패쥐를 소환할 수 없습니다.");
            return;
        }

        // [v14.7.10] 박스 컬라이더 기반 동적 위치 계산: 
        // 엘리트 본체의 BoxCollider2D 경계값(bounds)을 참조하여 겹치지 않는 가장 가까운 지점에 소환합니다.
        BoxCollider2D eliteCol = elite.obj.GetComponent<BoxCollider2D>();
        float spawnX = 0f;
        float padding = 0.2f; // 본체와 소환수 사이의 최소 간격

        if (eliteCol != null)
        {
            // 컬라이더의 왼쪽/오른쪽 끝단에서 padding 만큼 더 떨어진 곳에 배치
            spawnX = (index == 0) ? eliteCol.bounds.min.x - (padding + 0.3f) : eliteCol.bounds.max.x + (padding + 0.3f);
        }
        else
        {
            // 컬라이더가 없을 경우 기존 오프셋 방식 사용 (Safety Fallback)
            float xOffset = (index == 0) ? -1.2f : 1.5f;
            spawnX = elite.obj.transform.position.x + xOffset;
        }

        Vector3 spawnPos = new Vector3(spawnX, -3.6f, -1.0f);
        
        Debug.Log($"<color=yellow>[방패쥐 소환]</color> 인덱스: {index} | 위치: {spawnPos}");
        
        // [v14.7.7] MonsterSpawner의 전용 프리팹 사용
        GameObject prefab = (MonsterSpawner.Instance != null) ? MonsterSpawner.Instance.GetPrefabByID(data.id) : elite.obj;
        if (prefab == null) prefab = elite.obj;

        GameObject minionObj = Instantiate(prefab, spawnPos, Quaternion.identity);
        if (minionObj == null) return;

        // [v14.9.4] 네이밍 동기화: 데미지 흡수 로직이 '방패쥐 1', '방패쥐 2'를 찾으므로 index + 1 적용
        string mName = $"방패쥐 {index + 1}";
        minionObj.name = mName;

        // [v14.7.8] 스프라이터 레이어 순서 및 소팅 강제 보정 (기둥 가림 방지를 위해 500으로 설정)
        SpriteRenderer[] srs = minionObj.GetComponentsInChildren<SpriteRenderer>();
        int targetOrder = 500;

        foreach (var s in srs)
        {
            s.sortingLayerName = "Default";
            s.sortingOrder = targetOrder;
        }

        // [v14.7.9] 크기 조절: 생성된 인스턴스가 아닌 원본 프리팹(prefab)의 스케일을 참조하여 
        // 사용자가 에디터에서 설정한 크기를 정확히 가져온 뒤, 왼쪽을 향하도록 설정 (N01/N02와 동일하게 음수 적용)
        Vector3 curScale = prefab.transform.localScale;
        curScale.x = -Mathf.Abs(curScale.x); 
        minionObj.transform.localScale = curScale;

        // [v14.9.3] 소환된 방패쥐가 중력 때문에 땅 밑으로 꺼지지 않도록 고정
        Rigidbody2D rb = minionObj.GetComponent<Rigidbody2D>();
        if (rb != null) rb.gravityScale = 0f;

        MonsterClick mc = minionObj.GetComponent<MonsterClick>();
        if (mc == null) mc = minionObj.AddComponent<MonsterClick>();

        // 방패쥐 클릭 영역 확보
        BoxCollider2D bc = minionObj.GetComponent<BoxCollider2D>();
        if (bc == null) bc = minionObj.AddComponent<BoxCollider2D>();
        bc.isTrigger = true;
        bc.size = new Vector2(1.5f, 1.5f);

        MonsterInstance minion = new MonsterInstance(data, minionObj);
        minion.name = mName;
        minion.type = MonsterGrade.Normal1;
        minion.atk = 0; 
        minion.hp = UnityEngine.Random.Range(7, 10); 
        minion.maxHp = minion.hp;
        minion.isSummonedMinion = true; 

        activeMonsters.Add(minion);
        mc.LinkInstance(this, minion);

        // [추가] 소환된 방패쥐도 체력바가 나타나도록 UI 매니저에 알림
        if (UIEffectManager_C.Instance != null)
        {
            UIEffectManager_C.Instance.CreateMonsterHPBar(minion);
        }

        Debug.Log($"<color=cyan>[스마트 소환]</color> {mName}을(를) {(offset < 0 ? "왼쪽" : "오른쪽")} 자리에 소환했습니다.");
    }
    // 이름 규칙에 따라 등급을 리턴하는 헬퍼 메서드
    private MonsterGrade DetermineGradeFromName(string name)
    {
        string lowerName = name.ToLower();
        if (lowerName.Contains("boss")) return MonsterGrade.Boss;
        if (lowerName.Contains("elite")) return MonsterGrade.Elite;
        if (lowerName.Contains("normal2")) return MonsterGrade.Normal2;

        return MonsterGrade.Normal1; // 기본 등급
    }
    // Match3Manager에서 현재 이동 횟수를 조회하기 위한 public 함수
    public int GetCurrentMoveCount() => moveCount;
    // [핵심] Match3Manager에서 블록 매칭이 완료될 때마다 호출됨
    public void OnMoveCompleted(TurnResult result)
    {
       // 1.기초 수치 산출(기존 로직 유지)
         int calcAtk = (result.attack >= 3) ? 5 + (result.attack - 3) * 2 : 0;
        int calcDef = (result.defense >= 3) ? 5 + (result.defense - 3) * 2 : 0;
        int spcValue = (result.special >= 3) ? 3 + (result.special - 3) * 1 : 0;
        // 3. 아이템 효과가 적용된 최종 수치 계산
        // CalculateFinalDamage/Defense 내부에서 spcValue를 더해주고 있으므로 이를 활용합니다.
        float finalAttack = CalculateFinalDamage(calcAtk, result.special, spcValue);
        float finalDefense = CalculateFinalDefense(calcDef, result.special, spcValue);
        // 4. [수정 핵심] TurnResult에 '아이템이 적용된' 최종 값을 담아 savedResults에 저장
        // UI(ShowChoiceUI)는 savedResults의 데이터를 그대로 출력하므로 여기서 값이 확정되어야 합니다.
        TurnResult calculatedResult = new TurnResult(
            (int)finalAttack,
            (int)finalDefense,
            spcValue, // 특수 블록 개수 또는 계산된 spcValue를 넘김
            result.isFreeMove
        );
        // [110번 골드 정산 부분 수정]
        if (item110Reserved && !is110EffectUsedInThisBattle)
        {
            if (economyManager != null)
            {
                int goldGain = Mathf.FloorToInt(finalAttack);
                if (goldGain > 0)
                {
                    economyManager.AddGold(goldGain);
                    // ★ 중요: 사용 완료 플래그를 true로 설정하여 다음 4매치부터는 무시하게 함
                    is110EffectUsedInThisBattle = true;
                    Debug.Log($"<color=green>[Combat]</color> 110번 효과 발동 완료. 이후 전투 종료까지 재발동 불가.");
                }
            }
            item110Reserved = false;
            isWaitingForUI = false;
        }
        // 3. [추가] 선택 확정 시 합산 (이동 횟수 차감 및 보상 확정 시점)
        if (!result.isFreeMove)
        {
            // 보상을 선택하여 확정하는 시점에만 기존 방어도에 합산합니다.
            savedResults.Add(calculatedResult);

            moveCount++;
            
            // [보스 기믹] 오염 정화 수치 누적
            if (Match3Manager.Instance != null && Match3Manager.Instance.bossPhase > 0)
            {
                foreach (var m in activeMonsters)
                {
                    if (m.type == MonsterGrade.Boss)
                    {
                        m.removedContaminationTotal += Match3Manager.Instance.contaminationClearedInThisTurn;
                        Match3Manager.Instance.contaminationClearedInThisTurn = 0;
                    }
                }
            }

            UpdateMoveText();

            // 3. UI 출력 (UIEffectManager_C 연동)
            // 여기서 "총 데미지: XXX" 와 같은 텍스트가 출력됩니다.\
            if (moveCount >= 2) StartCoroutine(CheckTurnEndSequence());
            else
            {
                // 2턴이 아직 안되었다면 다시 조작 가능하게 해제
                isWaitingForUI = false;
            }
        }
    }
    // [특수 정산] 803번 아이템 등이 종료될 때 '한 번만' 호출됨
    // 사용자님의 의도: 이동횟수 UI에 수치를 출력하고 최종 정산하는 곳
    public void ProcessCombatResult(TurnResult result)
    {
        // 1. UI 출력 (이동 횟수 텍스트에 누적 수치 표시)
        if (moveText != null)
        {
            moveText.text = $"ATK:{result.attack} DEF:{result.defense}";
        }

        // 2. 최종 데미지 적용 (아이템 버프 합산)
        float finalDamage = result.attack + tempAttackBuff;
        if (finalDamage > 0 && selectedTarget != null)
        {
            ApplyDamageToMonster(selectedTarget, finalDamage);
        }

        currentShield += result.defense;

        // 3. 정산 후 버프 초기화
        tempAttackBuff = 0;
        UpdateStatus();
    }

    // 턴 변경 시 호출되는 핵심 로직에 추가
    public void SetPlayerTurn(bool isPlayer)
    {
        isPlayerTurn = isPlayer;

        // [최적화 연동] 매니저 간 직접 통신 최소화 및 상태 동기화
        if (Match3Manager.Instance != null)
        {
            // 플레이어 턴이고, UI 대기 상태가 아닐 때만 입력 허용
            bool canTouch = isPlayerTurn && !isWaitingForUI;
            Match3Manager.Instance.SetInteractable(canTouch);
        }
    }
    // 3. Match3에서 호출할 최종 확인 메서드
    public IEnumerator CheckTurnEndSequence()
    {
        if (moveCount >= 2)
        {
            isWaitingForUI = true;

           
            Debug.Log("<color=yellow>[Combat] 2회 이동 완료. 정산 UI를 출력합니다.</color>");

            // 블록이 다 떨어지는 걸 기다렸다가 UI를 띄우기 위해 약간의 지연
            yield return new WaitForSeconds(0.6f);
            ShowChoiceUI();
        }
    }
    // UI에서 버튼 선택 시 호출될 API (데이터 복구)
    // [헌법 3번 명세] 선택 완료 후 복구 API
    public void ResetMoveSelection(int additionalMoves)
    {
        moveCount = additionalMoves;
        savedResults.Clear(); // [추가] 다음 턴을 위해 리스트 비우기
        isWaitingForUI = false; // 조작 해제
        // UI 패널 닫기
        if (choiceUIPanel != null)
        {
            choiceUIPanel.SetActive(false);
        }
        UpdateMoveText();
        Debug.Log($"<color=cyan>[Combat]</color> 이동 횟수 충전 완료: {additionalMoves}");
    }
    //팝업 기능 딜레이
    public IEnumerator DelayedShowChoiceUI()
    {
        // 만약 여기서 return 되어버린다면 UI는 영원히 켜지지 않습니다.
        if (isWaitingForUI)
        {
            Debug.LogWarning("<color=yellow>[Combat] 이미 UI 대기 중이라 호출을 무시합니다.</color>");
            yield break;
        }
        isWaitingForUI = true;
        yield return new WaitForSeconds(0.5f);
        ShowChoiceUI();
    }
    // [신규] 이동 횟수 텍스트 갱신 (헌법 5번 과제 해결)
    public void UpdateMoveText()
    {
        if (moveText != null)
        {
            int remainingMoves = Mathf.Max(0, 2 - moveCount);
            moveText.text = $"MOVES: {remainingMoves}";
            
            // 남은 횟수가 0일 때 빨간색으로 강조
            moveText.color = (remainingMoves <= 0) ? Color.red : Color.white;
        }
        else
        {
            Debug.LogWarning("[Combat] moveText 참조가 없습니다. 인스펙터를 확인하세요.");
        }
    }
    // 유저에게 두 가지 매칭 결과 중 하나를 고르게 하는 UI 표시
    void ShowChoiceUI()
    {
        choiceUIPanel.SetActive(true); 
        option1TextAt.text = $": {savedResults[0].attack}";
        option1TextDf.text = $": {savedResults[0].defense}";

        option2TextAt.text = $": {savedResults[1].attack}";
        option2TextDf.text = $": {savedResults[1].defense}";
    }
    // 유저가 UI 버튼(Option 1 또는 2)을 클릭했을 때 실행
    public void SelectOption(int index)
    {
        // [v14.38] 버튼 클릭 효과음 재생
        if (AudioDirector.Instance != null) AudioDirector.Instance.PlayClick();

        // [수정] 인덱스 범위 확인 및 결과 할당
        if (savedResults == null || index >= savedResults.Count)
        {
            Debug.LogError("선택한 결과 데이터가 존재하지 않습니다!");
            return;
        }
        // 유저가 선택한 인덱스의 결과를 최종 수치로 확정
        finalSelectedResult = savedResults[index];
        // 아이템 보너스 합산 로직 호출 (기존 ExecuteCombat에 있던 로직을 분리하여 재사용 권장)
        // 최종 수치 확정
        // 최종 수치 확정
        // ✅ OnMoveCompleted에서 이미 아이템 적용된 값이 저장됨 → 재계산 금지
        _resolvedDamage = finalSelectedResult.attack;
        float totalDef = finalSelectedResult.defense;
        // ✅ 방어도 1회만 반영
        currentShield += totalDef;
        currentShield = Mathf.RoundToInt(currentShield);
        // 3. [중요] 방어도가 적용되었으므로 즉시 텍스트 UI 갱신
        UpdateShieldText(currentShield);

        // 4. 상태 변화를 다른 매니저(UIEffectManager_C 등)에도 알림
        UpdateStatus();
        // [중요: 데이터 리셋] 선택이 끝났으니 다음 턴을 위해 비워줍니다.
        moveCount = 0;           // 이동 횟수 리셋
        savedResults.Clear();    // 저장된 매칭 데이터 리셋
        UpdateMoveText();        // UI 텍스트도 0/2로 갱신
        // 선택창 닫기
        choiceUIPanel.SetActive(false);
        // [신규] 폭탄이 남아있으면 선택창 종료 후 폭탄 모드 재개
        if (Match3Manager.Instance != null && Match3Manager.Instance.bombCount > 0)
        {
            Match3Manager.Instance.ResumeBombMode();
            return; // 폭탄 재개 중이므로 몬스터 클릭 대기 로직은 스킵
        }

        // [801 로직 추가] 801 활성화 상태면 선택한 데미지 전체에 광역 공격 후 종료
        if (is801Active)
        {
            float aoeDamage = _resolvedDamage;
            Debug.Log($"<color=red>[801 화염의 물약 광역 폭발]</color> 전체 적에게 {aoeDamage} 피해를 줍니다.");

            for (int i = activeMonsters.Count - 1; i >= 0; i--)
            {
                ApplyDamageToMonster(activeMonsters[i], aoeDamage);
            }

            is801Active = false;
            _resolvedDamage = 0; // 광역 후 단일 타겟 클릭 대기 무효화
            
            // 전멸 체크 후 적 턴 전환 준비
            CheckAllMonstersDead();
            if (activeMonsters.Count > 0)
            {
                StartCoroutine(MonsterTurnCoroutine());
            }
            return;
        }

        // 이후 유저는 필드의 몬스터를 클릭하여 공격을 수행해야 함
        // [핵심 수정] 최종 데미지가 0이라면 몬스터 클릭 대기 없이 즉시 턴 종료
        if (_resolvedDamage <= 0)
        {
            Debug.Log("<color=green>공격력 0: 방어도만 적용 후 즉시 적 턴 전환.</color>");
            StartCoroutine(MonsterTurnCoroutine());
        }
        else
        {
            // ✅ 계산된 최종값을 ExecuteCombat에 직접 넘김
            Debug.Log($"<color=yellow>선택 완료!</color> 확정 데미지: {_resolvedDamage}. 공격할 몬스터를 클릭하세요!");
          
        }
    }
    // 유저가 몬스터 오브젝트를 클릭했을 때 호출 (MonsterClick 스크립트 연동)
    public void OnMonsterClicked(MonsterInstance clickedMonster)
    {
        Debug.Log($"<color=magenta>[디버그]</color> 몬스터 클릭 감지됨! (대상: {(clickedMonster != null ? clickedMonster.name : "NULL")})");
        Debug.Log($"<color=magenta>[디버그]</color> 현재 choiceUIPanel.activeSelf: {choiceUIPanel.activeSelf}, savedResults.Count: {savedResults.Count}, isPlayerTurn: {isPlayerTurn}");

        // [방어 로직] 선택창이 떠 있거나, 데미지 정산(2회 매칭)이 완료되지 않았으면 무시
        // 단, 공격 수치가 확정되었거나(_resolvedDamage > 0), 804번 아이템(짱돌) 사용 중일 때는 허용함.
        if (choiceUIPanel.activeSelf || (_resolvedDamage <= 0 && savedResults.Count < 2 && !is804Active))
        {
            Debug.LogWarning("아직 공격 준비(2회 매칭 완료 및 선택)가 되지 않았습니다.");
            return;
        }
        if (clickedMonster == null) return;

        // 타겟 설정 및 즉시 전투 실행 (매개변수 전달 방식 도입)
        selectedTarget = clickedMonster;
        Debug.Log($"<color=yellow>타겟 포착:</color> {selectedTarget.name}");
        if (is804Active)
        {
            Debug.Log($"<color=yellow>[804 적용]</color> {selectedTarget.id}에게 기절 예약.");
            selectedTarget.isStunned = true; // 몬스터에게 기절 플래그 삽입
            is804Active = false;     // 플래그 소모

            // 중요: 아이템 사용 후 즉시 적 턴으로 넘어가지 않도록 방어
            // 만약 현재 moveCount가 최대치라도, 아이템 사용 직후에는 플레이어가 
            // 상황을 인지할 수 있도록 약간의 여유를 주거나 UI 업데이트만 수행합니다.

            // 기절 이펙트 연출 트리거 (UIEffectManager_C 연동)
            //OnEffectTrigger?.Invoke("StunVisual");

            // 만약 아이템 사용이 '턴 소모'를 전제로 하지 않는다면 
            // 여기서 바로 CheckTurnEndSequence를 부르는 것이 아니라, 
            // 플레이어의 추가 조작(남은 이동 횟수 사용 등)을 기다려야 합니다.
            isWaitingForUI = false;

            // 현재 이동 횟수가 이미 다 찼더라도, '아이템 사용' 직후에 
            // 바로 넘어가는 것이 어색하다면 아래와 같이 처리합니다.
            if (moveCount >= 2)
            {
                // 선택창(SelectOption)이 이미 뜬 상태였다면 해당 흐름을 마무리
                // 아니라면 강제로 턴을 종료시키지 않고 사용자의 다음 행동을 대기
                Debug.Log("아이템 적용 완료. 남은 이동 횟수가 없으므로 정산을 확인하십시오.");
            }
            return;
        }
        // 타겟을 직접 인자로 넘겨서 함수 내부에서 null이 되는 것을 방지
        ExecuteCombat(selectedTarget);
    }
    // [추가] 방어도 텍스트를 갱신하는 전용 함수
    void UpdateShieldText(float shieldValue)
    {
        if (playercurshield != null)
        {
            playercurshield.text = shieldValue.ToString();
        }
    }
    // 실제로 데미지를 주고 몬스터의 턴으로 넘기는 과정
    // 매개변수로 타겟을 받도록 수정 (기존의 336행 에러 지점 보호)
    void ExecuteCombat(MonsterInstance target)
    {
        if (target == null)
        {
            Debug.LogError("공격 대상이 유효하지 않습니다.");
            return;
        }
        // ✅ 재계산 없이 캐싱된 확정 데미지만 사용
        // ✅ 캐싱된 확정값만 사용, 방어도는 SelectOption에서 이미 확정
        float finalDmg = _resolvedDamage;
        _resolvedDamage = 0f;

        // ✅ 방어도는 SelectOption에서 이미 반영 완료 — 여기서 건드리지 않음
        ApplyDamageToMonster(target, finalDmg);
        Debug.Log($"<color=red>[전투 실행]</color> {target.name}에게 {finalDmg} 데미지!");

        // 주의: 모든 몬스터가 죽었다면 적의 턴으로 넘어가면 안 됩니다.
        if (activeMonsters.Count > 0)
        {
            StartCoroutine(MonsterTurnCoroutine());
        }
    }
    // 타겟을 인자로 받아 처리하도록 변경
    void ApplyDamageToMonster(MonsterInstance target, float dmg)
    {
        if (target == null || target.hp <= 0) return;

        // [v14.7.5] 엘리트 관통 데미지 (고기방패) 로직 개편
        if (target.type == MonsterGrade.Elite)
        {
            float remainingDmg = dmg;
            Debug.Log($"<color=orange>[엘리트 타격]</color> 총 데미지 {dmg} 정산 시작 (본체 보호 중)");

            // 1. 방패쥐 1 (왼쪽) 검사 및 피해 흡수
            MonsterInstance s1 = activeMonsters.Find(m => m.isSummonedMinion && m.hp > 0 && m.name == "방패쥐 1");
            if (s1 != null && remainingDmg > 0)
            {
                float absorbed = Mathf.Min(s1.hp, remainingDmg);
                remainingDmg -= absorbed;
                ExecuteFinalDamage(s1, absorbed);
                Debug.Log($"<color=yellow>[흡수]</color> 방패쥐 1이 {absorbed} 피해를 흡수했습니다. (남은 데미지: {remainingDmg})");
            }

            // 2. 방패쥐 2 (오른쪽) 검사 및 추가 피해 흡수
            MonsterInstance s2 = activeMonsters.Find(m => m.isSummonedMinion && m.hp > 0 && m.name == "방패쥐 2");
            if (s2 != null && remainingDmg > 0)
            {
                float absorbed = Mathf.Min(s2.hp, remainingDmg);
                remainingDmg -= absorbed;
                ExecuteFinalDamage(s2, absorbed);
                Debug.Log($"<color=yellow>[흡수]</color> 방패쥐 2가 {absorbed} 피해를 흡수했습니다. (남은 데미지: {remainingDmg})");
            }

            // 3. 모든 방패쥐 소멸 후 남은 피해가 있다면 본체 적용
            if (remainingDmg > 0)
            {
                ExecuteFinalDamage(target, remainingDmg);
            }
        }
        else
        {
            // 엘리트가 아니면 즉시 데미지 적용
            ExecuteFinalDamage(target, dmg);
        }
    }

    /// <summary>
    /// [v14.7.5] 실제로 특정 몬스터의 HP를 깎고 UI 및 사망 처리를 수행하는 최종 관문
    /// </summary>
    private void ExecuteFinalDamage(MonsterInstance target, float dmg)
    {
        if (target == null || target.hp <= 0) return;

        // 1. 데미지 적용
        target.hp -= dmg;
        if (target.hp < 0) target.hp = 0;

        Debug.Log($"<color=red>[피격]</color> {target.name}에게 {dmg} 데미지 부여! (현재 HP: {target.hp})");
        if (AudioDirector.Instance != null) AudioDirector.Instance.PlayPlayerHit();

        // 2. 보스 기믹 처리 (2페이즈 전환)
        if (target.type == MonsterGrade.Boss && target.bossPhase == 1 && target.hp <= 100)
        {
            target.bossPhase = 2;
            target.hp = 100;
            if (Match3Manager.Instance != null)
            {
                Match3Manager.Instance.bossPhase = 2;
                Match3Manager.Instance.TriggerRatInvasion(Match3Manager.Instance.width * Match3Manager.Instance.height);
                Match3Manager.Instance.ShakeCamera(1.0f, 0.2f);
            }
            Debug.Log("<color=red>[보스 기믹]</color> 보스 2페이즈 발동!");
        }
        int popupDamage = Mathf.RoundToInt(dmg);
        if (popupDamage > 0 && target.obj != null)
        {
            OnMonsterDamagePopupRequested?.Invoke(popupDamage, target.obj.transform.position, dmg >= 30f);

            // [v14.37] 크리티컬 연출 판정 (30데미지 이상)
            if (dmg >= 30f)
            {
                OnEffectTrigger?.Invoke("Critical", target.obj.transform.position);
            }
        }
        // 3. 유저 아이템 효과 (104/304. 붉은 송곳니 흡혈)
        int fangId = GetActualID(104);
        if (fangId != -1)
        {
            float healRate = economyManager.GetItemInfo(fangId).effectValue / 100f;
            playerHP = Mathf.Min(maxHP, playerHP + (dmg * healRate));
            UpdateStatus();
            Debug.Log($"<color=lime>[Passive {fangId}]</color> 흡혈 {dmg * healRate} 적용.");
        }

        // 4. UI 업데이트 및 사망 처리
        int monsterIndex = activeMonsters.IndexOf(target);
        if (monsterIndex != -1)
        {
            OnMonsterHealthChanged?.Invoke(monsterIndex, target.hp);
        }

        if (target.hp <= 0)
        {
            Debug.Log($"<color=orange><b>[처치] {target.name} 폐기.</b></color>");
            if (AudioDirector.Instance != null) AudioDirector.Instance.PlayDeath();
            RegisterDefeatedMonster(target);
            
            // [추가] 사망 시 UI 매니저에서 해당 몬스터의 HP바 제거
            if (UIEffectManager_C.Instance != null && target.obj != null)
            {
                UIEffectManager_C.Instance.RemoveMonsterUI(target.obj);
            }

            activeMonsters.Remove(target);
            if (target.obj != null) Destroy(target.obj);
            CheckAllMonstersDead();
        }
    }
    // [신규 추가] 모든 몬스터 사망 확인 함수
    private void CheckAllMonstersDead()
    {
        // ApplyDamageToMonster에서 이미 제거 완료된 상태이므로
        // 여기서는 activeMonsters가 비었는지만 확인
        if (activeMonsters.Count == 0)
        {
            Debug.Log("<color=cyan><b>[전투 종료] 모든 적을 섬멸했습니다! 승리!</b></color>");

            // [엔딩 분기 체크] 남은 오염 블록 수에 따라 엔딩 타입 결정
            if (Match3Manager.Instance != null && GameFlowManager.Instance.currentStageType == StageType.Boss)
            {
                int remainingContamination = Match3Manager.Instance.GetContaminatedCount();
                if (remainingContamination == 0)
                {
                    Debug.Log("<color=lime>[최종 결과] 모든 오염을 정화하고 승리했습니다! (True Ending 조건 만족)</color>");
                }
                else
                {
                    Debug.Log($"<color=orange>[최종 결과] 적은 전멸시켰으나, 오염 블록이 {remainingContamination}개 남아있습니다. (Normal/Bad Ending 조건)</color>");
                }
            }

            ResetBattleBuffs();
            StartCoroutine(CoEndBattleWithRewards());
        }
    }
    // [4] 전투 종료 및 보상 지급 프로세스
    private IEnumerator CoEndBattleWithRewards()
    {
        Debug.Log("<color=green>[전투 승리]</color> 정산을 시작합니다.");
        yield return new WaitForSeconds(1.0f); // 연출용 대기
        if (GameFlowManager.Instance != null && GameFlowManager.Instance.currentStageType == StageType.Boss)
        {
            Debug.Log("<color=magenta>[Boss Victory]</color> 보스전 승리! 보상 선택을 생략하고 스토리로 이동합니다.");

            // 보상 선택 과정을 건너뛰고 바로 다음 단계로 이동
            OnCombatFinished?.Invoke(true);

            // 스토리 씬이나 다음 맵으로 이동 (프로젝트 기획에 맞춰 StageType 수정 가능)
            Match3Manager.Instance.FinalEndingCheck();
            yield break;
        }

        // 2. 골드 계산
        int totalGold = CalculateFinalGoldReward();
        isWaitingForUI = true;

        // 3. 스테이지 타입에 따른 보상 풀 분기 [헌법 §68]
        List<int> rewardItems = new List<int>();

        if (EconomyManager.Instance != null && GameFlowManager.Instance != null)
        {
            StageType currentStage = GameFlowManager.Instance.currentStageType;

            if (currentStage == StageType.EliteBattle)
            {
                // 엘리트 노드인 경우 엘리트 보상 API 호출
                Debug.Log("<color=cyan>[Elite Reward]</color> 엘리트 전용 보상 리스트를 추출합니다.");
                rewardItems = EconomyManager.Instance.DequeueEliteRewards(3);
            }
            else
            {
                // 일반 전투(Battle)인 경우 일반 보상 API 호출
                Debug.Log("<color=white>[General Reward]</color> 일반 보상 리스트를 추출합니다.");
                rewardItems = EconomyManager.Instance.DequeueGeneralRewards(3);
            }
        }

        // 4. 폴백 (데이터 부재 시 최소 보상 보장)
        if (rewardItems == null || rewardItems.Count == 0)
        {
            Debug.LogWarning("[Combat] 보상 풀이 비어있어 기본 아이템으로 대체합니다.");
            rewardItems = new List<int> { 101, 102, 103 };
        }

        // 5. UI 호출 [헌법 §3 API 준수]
        // Combat -> UIEffectManager_C로 보상 데이터 전송
        OnShowRewardPicker?.Invoke(rewardItems, totalGold);
    }

    public void OnRewardClaimed()
    {
        Debug.Log("<color=cyan>[보상 수령 완료]</color> 전투 종료 프로세스 완료.");
        isWaitingForUI = false;
        OnCombatFinished?.Invoke(true);
        if (GameFlowManager.Instance != null)
        {
            GameFlowManager.Instance.EnterStage(StageType.StageSelect);
        }
    }
    // [핵심 추가] 턴 종료 시 추가 공격 (IncrementTurnBuff 또는 별도 페이즈에서 호출)
    public void ExecuteRatKnightAttack()
    {
        if (currentRatKnightStack <= 0) return;

        int bonusDmg = currentRatKnightStack * 4;
        Debug.Log($"<color=orange>[703 기사단 반격]</color> 남은 기사단 {currentRatKnightStack}기 → {bonusDmg} 추가 데미지!");

        MonsterInstance target = (selectedTarget != null && selectedTarget.hp > 0)
            ? selectedTarget
            : (activeMonsters.Count > 0 ? activeMonsters[0] : null);

        if (target != null)
            ApplyDamageToMonster(target, bonusDmg);

        currentRatKnightStack = 0;
    }
    // 몬스터들이 순차적으로 공격하는 루틴
    /// <summary>
    /// 모든 몬스터가 순차적으로 플레이어를 공격하는 코루틴 (아이템 효과 통합 버전)
    /// </summary>
    public IEnumerator MonsterTurnCoroutine()
    {
        Debug.Log("<color=red>[적 턴 시작]</color>");
        isPlayerTurn = false; // 여기서 입력을 차단하게 됨
        // 공격 전 연출 대기 시간
        yield return new WaitForSeconds(1.0f);

        // [수정] foreach → 역순 for. 반사 데미지로 몬스터 사망 시 리스트 변경 안전 처리
        for (int i = activeMonsters.Count - 1; i >= 0; i--)
        {
            if (i >= activeMonsters.Count) continue; // 앞 순번에서 제거된 경우 방어
            MonsterInstance monster = activeMonsters[i];

            if (monster.hp <= 0) continue;

            // [v14.6.9] 소환된 쫄몹은 공격 루틴을 수행하지 않음 (로그 및 데미지 차단)
            if (monster.isSummonedMinion) continue;

            // [804/기절] 기절 상태면 특수 기믹(소환 등)과 공격 모두 스킵 후 기절 해제
            if (monster.isStunned)
            {
                Debug.Log($"<color=gray>[기절]</color> {monster.name}은(는) 기절 상태라 행동(소환/공격)을 하지 못합니다!");
                monster.isStunned = false; // 1턴 한정이므로 즉시 해제
                continue;
            }

            if (monster.type == MonsterGrade.Elite)
            {
                monster.summonCooldownCounter++;
                Debug.Log($"<color=cyan>[Charge Debug]</color> Elite {monster.name} Charge: {monster.summonCooldownCounter}/3");
                OnMonsterChargeChanged?.Invoke(monster.obj, monster.summonCooldownCounter, 3);
                
                if (monster.summonCooldownCounter >= 3)
                {
                    // [v14.7.4] 3/3 정비 턴: 방패쥐 결손 여부 확인
                    bool s1Alive = activeMonsters.Exists(m => m.isSummonedMinion && m.hp > 0 && m.name == "방패쥐 1");
                    bool s2Alive = activeMonsters.Exists(m => m.isSummonedMinion && m.hp > 0 && m.name == "방패쥐 2");

                    if (!s1Alive || !s2Alive)
                    {
                        Debug.Log($"<color=red>[엘리트 정비]</color> {monster.name}이(가) 부족한 방패쥐를 재소환합니다! (공격 스킵)");
                        SummonEliteMinions(monster);
                        // monster.summonCooldownCounter = 0; // [v14.37.1] 아래로 이동
                        yield return new WaitForSeconds(0.8f);
                        
                        // [v14.37.1] 정비 후 리셋
                        monster.summonCooldownCounter = 0;
                        OnMonsterChargeChanged?.Invoke(monster.obj, 0, 3);
                        
                        continue; // ★ 정비 턴에는 공격하지 않고 다음 몬스터로 넘어감
                    }
                    else
                    {
                        Debug.Log($"<color=orange>[엘리트]</color> {monster.name} 소환 대기 중... (3/3) - 방패쥐가 모두 건재하여 공격을 수행합니다!");
                        // monster.summonCooldownCounter = 0; // [v14.37.1] 아래로 이동
                    }
                }
                else
                {
                    Debug.Log($"<color=orange>[엘리트]</color> {monster.name} 소환 대기 중... ({monster.summonCooldownCounter}/3)");
                }
            }


            // --- [아이템 효과] 105/305. 매끈 참기름 (회피) ---
            int oilId = GetActualID(105);
            if (oilId != -1)
            {
                float avoidChance = economyManager.GetItemInfo(oilId).effectValue;
                if (UnityEngine.Random.Range(0f, 100f) < avoidChance)
                {
                    Debug.Log($"<color=cyan>[회피 {oilId}]</color> 공격을 피했습니다!");
                    continue; 
                }
            }

            float damageAmount = monster.atk;
            // 반사 데미지 계산용 (실제 체력이 깎일 수치 예측)
            float damageToTakeCalculated = Mathf.Max(0, damageAmount - currentShield);

            // --- [아이템 효과] 107/307. 까칠하쥐 (반사) ---
            int prickId = GetActualID(107);
            if (prickId != -1 && damageToTakeCalculated > 0)
            {
                float reflectRate = economyManager.GetItemInfo(prickId).effectValue / 100f;
                float reflectDamage = damageToTakeCalculated * reflectRate;

                MonsterInstance reflectTarget = monster;
                // [고기방패 체크] 엘리트일 경우 방패쥐가 대신 반사 데미지를 받음
                if (monster.type == MonsterGrade.Elite)
                {
                    var minion = activeMonsters.Find(m => m.isSummonedMinion && m.hp > 0);
                    if (minion != null)
                    {
                        Debug.Log($"<color=orange>[반사 방어]</color> {minion.name}이 엘리트 대신 반사 피해를 입습니다.");
                        reflectTarget = minion;
                    }
                }

                ExecuteFinalDamage(reflectTarget, reflectDamage);
                Debug.Log($"<color=orange>[반사 {prickId}]</color> {reflectDamage} 피해가 {reflectTarget.name}에게 반사됨.");
            }

            // --- [공격] 보스 전용 기믹 처리 ---
            if (monster.type == MonsterGrade.Boss)
            {
                monster.bossChargeCount++;
                Debug.Log($"<color=cyan>[Charge Debug]</color> Boss {monster.name} Charge: {monster.bossChargeCount}/2");
                OnMonsterChargeChanged?.Invoke(monster.obj, monster.bossChargeCount, 2);

                if (monster.bossChargeCount < 2)
                {
                    string statusText = (monster.bossPhase == 1) ? "강화 공격 충전 중..." : "자폭 에너지 집중 중...";
                    Debug.Log($"<color=orange>[보스 상태]</color> {monster.name}이(가) {statusText} (카운트: {monster.bossChargeCount}/2)");

                    if (Match3Manager.Instance != null) Match3Manager.Instance.ShakeCamera(0.3f, 0.05f);
                    yield return new WaitForSeconds(0.5f);
                    continue; // 1턴째에는 공격을 하지 않고 턴을 넘깁니다.
                }
                else
                {
                    // 2턴째: 충전 완료 및 공격
                    if (monster.bossPhase == 1)
                    {
                        Debug.Log($"<color=red>[충전 완료!]</color> {monster.name}의 강력한 일격이 쏟아집니다!");
                        damageAmount = 20 - (monster.removedContaminationTotal * 2);
                        damageAmount = Mathf.Max(0, damageAmount);
                        monster.removedContaminationTotal = 0;
                    }
                    else
                    {
                        Debug.Log($"<color=red>[붕괴 발동!]</color> 스테이지의 무너진 잔해가 쏟아집니다!");
                        damageAmount = 20f; // [v14.5.6 상향] 10 -> 20
                        if (Match3Manager.Instance != null)
                        {
                            Match3Manager.Instance.ShakeCamera(0.8f, 0.2f);
                        }
                        // 사용자의 피드백에 따라 보스는 공격 후에도 자동으로 죽지 않고 공격을 지속합니다.
                    }
                    // monster.bossChargeCount = 0; // [v14.37.1] 아래로 이동
                }
            }

            Debug.Log($"<color=orange>[공격]</color> {monster.name}이(가) {damageAmount}의 공격력으로 공격합니다!");

            // [v14.51] 공격 모션 연출 (앞으로 나갔다 돌아오기)
            yield return StartCoroutine(PlayMonsterAttackMotion(monster.obj));

            // --- [기사단 방어 가로채기] ---
            if (currentRatKnightStack > 0)
            {
                // 헌법 3조 준수: 데미지 우선 차감 및 무효화
                currentRatKnightStack--;
                Debug.Log("<color=blue>[기사단] 적의 공격을 방어했습니다! (남은 스택: " + currentRatKnightStack + ")</color>");

                // [신규] 공격을 막아냈으므로 다음 턴 공격 준비
                wasKnightActiveLastTurn = true;

                // 이번 몬스터 데미지는 0처리 (기존 로직 유지)
                damageAmount = 0;
            }
            else
            {
                wasKnightActiveLastTurn = false;
            }
            // 1. 방어막 처리 로직
            float shieldBeforeHit = currentShield;
            if (currentShield > 0)
            {
                if (currentShield >= damageAmount)
                {
                    currentShield -= damageAmount;
                    damageAmount = 0;
                    Debug.Log($"방어막으로 모든 공격을 막았습니다. 남은 방어막: {currentShield}");
                }
                else
                {
                    damageAmount -= (int)currentShield;
                    currentShield = 0;
                    Debug.Log("방어막이 파괴되었습니다! 남은 데미지가 체력에 적용됩니다.");
                }
            }
            currentShield = Mathf.Max(0, currentShield);

            // [v14.53] 방어막 피격 팝업 요청
            int actualShieldLoss = Mathf.RoundToInt(shieldBeforeHit - currentShield);
            if (actualShieldLoss > 0)
            {
                OnPlayerShieldDamagePopupRequested?.Invoke(actualShieldLoss);
                // [v14.54] 방어막 팝업이 먼저 보이도록 약간의 지연 추가
                yield return new WaitForSeconds(0.3f);
            }

            // 2. 남은 데미지를 플레이어 체력(HP)에 적용
            float hpBeforeHit = playerHP; // [v14.14] 팝업용 HP 변화량 계산용
            if (damageAmount > 0)
            {
                playerHP -= damageAmount;

                // --- [아이템 효과] 112/312. 끈쥘김 (사망 방지) ---
                int persistId = GetActualID(112);
                if (playerHP <= 0 && persistId != -1)
                {
                    playerHP = 1;
                    economyManager.RemoveItemFromInventory(persistId); // 소모
                    Debug.Log($"<color=yellow>[부활 {persistId}]</color> 체력 1로 버텼습니다!");
                }

                if (playerHP < 0) playerHP = 0;
                Debug.Log($"플레이어 피격! 남은 체력: {playerHP}");
                if (AudioDirector.Instance != null) AudioDirector.Instance.PlayPlayerHit();
            }

            // 3. UI 갱신 (방어막/체력 변화 알림 및 Flow 백업)
            UpdateStatus();
            UpdateShieldText(currentShield);

            // [v14.14] 플레이어 피격 팝업: 실제 HP 손실량만 표시 (방어막 흡수분 제외)
            int actualHpLoss = Mathf.RoundToInt(hpBeforeHit - playerHP);
            if (actualHpLoss > 0)
            {
                OnPlayerDamagePopupRequested?.Invoke(actualHpLoss);
            }

            // 플레이어 사망 체크
            if (playerHP <= 0)
            {
                Debug.Log("<color=black>플레이어 사망...</color>");
                ResetBattleBuffs();
                if (GameFlowManager.Instance != null)
                {
                    GameFlowManager.Instance.OnPlayerDeath();
                }
                OnCombatFinished?.Invoke(false);
                yield break;
            }

            yield return new WaitForSeconds(0.5f);

            // [v14.37.5] 차징 수치 리셋 (데미지 처리 후 충분한 대기 후 리셋)
            if (monster.type == MonsterGrade.Elite && monster.summonCooldownCounter >= 3)
            {
                yield return new WaitForSeconds(0.5f); // 공격/소환 연출을 위한 추가 대기
                Debug.Log($"<color=cyan>[Charge Debug]</color> Elite {monster.name} Resetting Charge to 0");
                monster.summonCooldownCounter = 0;
                OnMonsterChargeChanged?.Invoke(monster.obj, 0, 3);
            }
            if (monster.type == MonsterGrade.Boss && monster.bossChargeCount >= 2)
            {
                yield return new WaitForSeconds(0.5f); // 보스 공격 연출을 위한 추가 대기
                Debug.Log($"<color=cyan>[Charge Debug]</color> Boss {monster.name} Resetting Charge to 0");
                monster.bossChargeCount = 0;
                OnMonsterChargeChanged?.Invoke(monster.obj, 0, 2);
            }
        }

        // --- 모든 몬스터 공격 종료 후 처리 ---

        // --- [아이템 효과] 108/308. 적자생존 (방어막 유지) ---
        int survivalId = GetActualID(108);
        if (survivalId != -1 && currentShield > 0)
        {
            float retainRate = economyManager.GetItemInfo(survivalId).effectValue / 100f;
            currentShield = Mathf.RoundToInt(currentShield * retainRate);
            Debug.Log($"<color=cyan>[Passive {survivalId}]</color> 방어막 {retainRate*100}% 유지 -> {currentShield}");
        }
        else
        {
            // 아이템이 없으면 턴 종료 시 방어막 초기화
            currentShield = 0;
        }
        // 턴 종료 처리 (방어막 유지 로직 아래)
        UpdateShieldText(currentShield);
        UpdateStatus();

        // [추가] 플레이어에게 주도권이 넘어가기 전 1턴 한정 버프 삭제
        ResetTurnTemporaryBuffs(); // 여기서 호출!

        // [109] 나쥐 효과: 턴 종료마다 공격력 누적 버프 증가
        IncrementTurnBuff();

        // [703] 턴 종료 시 남은 기사단 반격 실행
        ExecuteRatKnightAttack();

        // [v14.5.6 수정] 사용자의 요청에 따라 오염 제거 시 자동 승리 판정을 제거합니다.
        // 전투의 종료는 반드시 CheckAllMonstersDead()에서 '전멸' 시점에만 이루어집니다.

        Debug.Log("<color=green>[적 턴 종료] 플레이어의 턴으로 돌아갑니다.</color>");
        StartPlayerTurn();
    }
    // [신규] 플레이어 턴 시작 시 호출 (공격력 부여)
    public void ApplyKnightCounterAttack()
    {
        if (wasKnightActiveLastTurn)
        {
            // 다음 공격에 데미지 4 추가 (기사단 반격)
            // 현재 로직의 '플레이어 기본 공격력' 변수에 +4 처리를 수행하세요.
            Debug.Log("<color=red>[기사단 반격] 지난 턴 생존 성공! 공격력 4가 추가됩니다.</color>");
            wasKnightActiveLastTurn = false; // 효과 소모
        }
    }
    public void EndPlayerTurn()
    {
        Debug.Log("<color=green>[턴 종료] 플레이어 행동 완료. 적 턴 시작.</color>");
        // 기존에 존재하는 MonsterTurnCoroutine을 실행하여 턴을 넘깁니다.
        StartCoroutine(MonsterTurnCoroutine());
    }

    // 만약 폭탄 데미지를 적용하고 싶다면 아래와 같이 활용 가능합니다.
    public void ProcessBombDamage(int damage)
    {
        // 현재 전투 중인 모든 몬스터에게 데미지 전달 로직 (필요 시)
        // 그 후 턴 종료
        EndPlayerTurn();
    }
    // CombatManager.cs 내부에 추가/수정
    // 801: 기존 ExecuteItem801 활용
    // CombatManager.cs 313행 부근 수정
    // CombatManager.cs 교정본
    public void ExecuteItem801()
    {
        // ※ UseItem()에서 이미 보유 검증 및 소모 완료 후 호출됨 — 중복 체크 불필요

        // 헌법 4-1: 수치 직접 가산
        tempAttackBuff += 10f;
        is801Active = true; // 플래그 활성화

        Debug.Log("<color=green>[아이템 발동]</color> 801번 화염의 물약 효과가 정상 적용되었습니다. 턴 선택 시 광역 공격이 발동됩니다.");
    }
    // 802: 황금사과 (체력 30% 회복)
    // CombatManager.cs 내부 (기존 partial class CombatManager 안에 작성)
    // CombatManager.cs 메인 파일 내부에 추가
    public void ApplyHeal(float amount)
    {
        // 헌법 4-1: 직접 가산 원칙
        playerHP += amount;

        // 최대 체력 초과 방지 (헌법 4-4: 확정적 수치 관리)
        if (playerHP > maxHP) playerHP = maxHP;

        // [v14.7.9] 통합 상태 갱신 및 Flow 동화
        UpdateStatus();

        Debug.Log($"<color=lime>[Combat 수신]</color> 회복 적용됨. 현재 HP: {playerHP}");
    }

    // [UI 연동] 휴식 시 비율 단위로 체력을 회복하는 메서드 추가
    public void ProcessRestHeal(float ratio)
    {
        float healAmount = maxHP * ratio;
        ApplyHeal(healAmount);
        Debug.Log($"<color=lime>[휴식]</color> 최대 체력의 {ratio*100}% 회복 완료.");
    }
    // [v11.1] 803번 아이템 종료 시 Match3Manager에서 호출
    // 10초 정산 결과를 선택창 2개 옵션으로 직접 주입하고 이동 1회 소모 처리
    // CombatManager.cs
    // Inject803Result 전면 교체 — 단일 결과, 선택창 없이 바로 확정
    // CombatManager.cs - Inject803Result 최종 교체
    public void Inject803Result(TurnResult raw803Result)
    {
        // 1. 계산 공식 1회만 적용 (OnMoveCompleted와 동일한 공식)
        int calcAtk = (raw803Result.attack >= 3) ? 5 + (raw803Result.attack - 3) * 2 : 0;
        int calcDef = (raw803Result.defense >= 3) ? 5 + (raw803Result.defense - 3) * 2 : 0;
        int spcValue = (raw803Result.special >= 3) ? 3 + (raw803Result.special - 3) * 1 : 0;

        float finalAtk = CalculateFinalDamage(calcAtk, raw803Result.special, spcValue);
        float finalDef = CalculateFinalDefense(calcDef, raw803Result.special, spcValue);

        // 2. [핵심 수정] 방어도는 여기서 반영하지 않음
        //    SelectOption처럼 savedResults에 넣고 선택창을 통해 반영
        //    → 단, 803은 선택지가 1개이므로 동일값 2개로 채워 선택창 정상 작동
        TurnResult confirmed = new TurnResult((int)finalAtk, (int)finalDef, spcValue, false);
        while (savedResults.Count < moveCount)
        {
            // 앞 자리가 비어있으면 빈 결과로 채움 (방어적 처리)
            savedResults.Add(new TurnResult(0, 0, 0, false));
        }
        savedResults.Add(confirmed); // 이제 정확히 [moveCount] 자리에 들어감
        // 3. [핵심 수정] moveCount = 2로 강제 세팅
        //    → 이미 2회 소모한 것으로 처리, 이후 일반 드래그 차단
        moveCount++;
        UpdateMoveText();
        Debug.Log($"<color=purple>[803 확정]</color> 최종 공격: {finalAtk}, 최종 방어: {finalDef}");
        if (moveCount >= 2)
        {
            // 이미 1회 이동이 있었던 상태 → 즉시 선택창
            isWaitingForUI = true;
            StartCoroutine(CheckTurnEndSequence());
        }
        else
        {
            // 0회였던 상태 → 일반 드래그 1회 더 대기
            Debug.Log("<color=yellow>[803] 일반 드래그 1회 더 하세요.</color>");
        }
    }
    // 804: 짱돌 (선택된 타겟 1턴 기절)
    public void ExecuteItem804()
    {
        // 사용 시점에 타겟을 결정하지 않음
        // 이동횟수 선택 후 몬스터 클릭 시점에 기절 적용
        is804Active = true;
        isWaitingForUI = true; // 몬스터를 선택할 때까지 보드 조작 및 자동 턴 종료 방지
        Debug.Log("<color=gray>[804 짱돌]</color> 준비 완료! 공격할 몬스터를 선택하면 기절 효과가 적용됩니다.");
        // [연결 지점: UIEffectManager_C]
        // 몬스터 머리 위에 기절 아이콘이나 이펙트(isStunned 시각화) 요청
       // OnEffectTrigger?.Invoke("Stun_Visual");
    }
    // CombatManager.cs (Partial)
    public void ExecuteItem110()
    {
        item110Reserved = true;
        Debug.Log("<color=orange>[Combat]</color> 110번 아이템 정산 예약 접수.");
    }
    // 실제 데미지가 확정되는 지점(예: OnMoveCompleted 내부 계산 직후)에서 호출
    private void ProcessItem110Gold(float finalDamage)
    {
        if (item110Reserved)
        {
            if (economyManager != null)
            {
                // [기획 수정] 데미지 수치만큼 100% 골드 획득
                int goldAmount = Mathf.FloorToInt(finalDamage);

                if (goldAmount > 0)
                {
                    economyManager.AddGold(goldAmount);
                    Debug.Log($"<color=yellow>[Item 110]</color> 4매치 보너스! 데미지({goldAmount})만큼 골드 획득.");
                }
            }
            item110Reserved = false; // 처리 완료 후 예약 해제
        }
    }
    // 몬스터 사망 시 호출 (MonsterInstance에서 실행)
    public void RegisterDefeatedMonster(MonsterInstance monster)
    {
        if (monster == null || monster.isSummonedMinion) return; // [v14.5.9] 쫄몹은 보상 기록 제외

        defeatedMonsterGrades.Add(monster.type);
        Debug.Log($"<color=white>[Combat]</color> {monster.type} 처치 기록됨.");
    }
    // 전투 승리 시 호출될 최종 골드 정산 메서드
    private int CalculateFinalGoldReward()
    {
        if (defeatedMonsterGrades.Count == 0) return 0;

        int totalGold = 0;
        foreach (var grade in defeatedMonsterGrades)
        {
            // 각 등급별 정의된 골드 범위 내에서 랜덤 추출
            totalGold += grade switch
            {
                MonsterGrade.Normal1 => UnityEngine.Random.Range(3, 6),   // 3~5
                MonsterGrade.Normal2 => UnityEngine.Random.Range(5, 9),   // 5~8
                MonsterGrade.Elite => UnityEngine.Random.Range(17, 22), // 17~21
                MonsterGrade.Boss => UnityEngine.Random.Range(20, 24), // 20~23
                _ => 0
            };
        }
        Debug.Log($"<color=yellow>[골드 정산]</color> 이번 전투 총 획득: {totalGold}G");
        // [수정 포인트] 계산된 결과를 EconomyManager에 반영
        // [수정] EconomyManager.Instance 대신 인스펙터에 연결된 economyManager 변수 사용
        if (economyManager != null)
        {
            economyManager.AddGold(totalGold);
            Debug.Log("<color=green>[Combat]</color> 연결된 economyManager를 통해 골드 전달 완료.");
        }
        else
        {
            // 만약 이 로그가 뜬다면 인스펙터 할당이 풀린 것입니다.
            Debug.LogError("CombatManager의 economyManager 필드가 비어있습니다! 인스펙터를 확인하세요.");
        }

        defeatedMonsterGrades.Clear();
        return totalGold;
    }
    // Match3에서 호출할 예약 메서드
    public void ReserveItem110Effect()
    {
        // 이미 이번 전투에서 사용했다면 예약을 무시함
        if (is110EffectUsedInThisBattle) return;

        item110Reserved = true;
        Debug.Log("<color=yellow>[Combat]</color> 110번 아이템 효과 예약됨 (이번 전투 최초 1회)");
    }

    // [v14.7.6] 아이템 강화 호환 헬퍼: 일반(id) 또는 강화(id+200) 중 보유 중인 ID 반환
    private int GetActualID(int baseId)
    {
        if (economyManager == null) return -1;
        if (economyManager.ownedItemIDs.Contains(baseId + 200)) return baseId + 200;
        if (economyManager.ownedItemIDs.Contains(baseId)) return baseId;
        return -1;
    }

    // [v14.7.9] 통합 상태 갱신 헬퍼 (UI 알림 + GameFlowManager 동기화)
    private void UpdateStatus()
    {
        OnPlayerStatusChanged?.Invoke(playerHP, currentShield);

        if (GameFlowManager.Instance != null)
        {
            GameFlowManager.Instance.UpdateStatusFromCombat(playerHP, currentShield);
        }
    }
    // [v14.89] 임시 디버그용 키 입력 및 기능 (기존 코드 유지 후 없는 코드만 추가)
    void Update()
    {
        // [명령 수행] F1 키 입력 시 디버그 아이템 획득 (정식 루트)
        if (Input.GetKeyDown(KeyCode.F1))
        {
            int debugItemID = 701; // 테스트용 아이템 ID
            if (economyManager != null && !economyManager.HasItem(debugItemID))
            {
                economyManager.AddItem(debugItemID);
                Debug.Log($"<color=yellow>[디버그 획득]</color> {debugItemID}번 아이템 정식 지급 완료!");
                PrintInventoryStatus();
            }
        }
        // [명령 수행] F2 키 입력 시 디버그 아이템 획득 (정식 루트)
        if (Input.GetKeyDown(KeyCode.F2))
        {
            int debugItemID = 702; // 테스트용 아이템 ID
            if (economyManager != null && !economyManager.HasItem(debugItemID))
            {
                economyManager.AddItem(debugItemID);
                Debug.Log($"<color=yellow>[디버그 획득]</color> {debugItemID}번 아이템 정식 지급 완료!");
                PrintInventoryStatus();
            }
        }
        // [신규] I 키 입력 시 인벤토리 브리핑 실행
        if (Input.GetKeyDown(KeyCode.I))
        {
            if (economyManager != null)
            {
                economyManager.ShowInventoryLog();
            }
        }
        if (Input.GetKeyDown(KeyCode.Alpha7))
        {
            Debug.Log("<color=red>[Debug]</color> 전투 스킵 키 입력됨!");
            SkipCombatAndShowReward();
        }
        if (Input.GetKeyDown(KeyCode.Alpha9))
        {
            KillAllMonstersCheat();
        }

    }

    private void KillAllMonstersCheat()
    {
        if (activeMonsters == null || activeMonsters.Count == 0) return;

        Debug.Log("<color=yellow>[Cheat]</color> 모든 몬스터에게 9999 데미지를 입혀 즉시 승리합니다.");

        // 리스트를 복사해서 순회해야 안전합니다 (ExecuteFinalDamage 내부에서 Remove가 일어나기 때문)
        List<MonsterInstance> monstersToKill = new List<MonsterInstance>(activeMonsters);

        foreach (var monster in monstersToKill)
        {
            if (monster != null && monster.hp > 0)
            {
                // [핵심] 단순히 HP를 0으로 깎는게 아니라, 기존의 데미지 처리 함수를 호출합니다.
                // 그래야 사망 판정 -> CheckAllMonstersDead() -> CoEndBattleWithRewards()가 차례로 실행됩니다.
                ExecuteFinalDamage(monster, 9999f);
            }
        }
    }

    private void SkipCombatAndShowReward()
    {
        // 1. 모든 코루틴 중지 (몬스터 공격 등 방지)
        StopAllCoroutines();

        // 2. 현재 남아있는 모든 몬스터 즉시 제거 로직
        // (이미 구현된 몬스터 리스트가 있다면 모두 0으로 만들거나 파괴)

        // 3. 전투 승리 처리 및 보상 팝업 호출
        // CombatManager에 이미 구현된 보상 팝업 함수를 호출합니다.
        // 보통 골드 정산과 아이템 리스트 생성이 포함됩니다.

        int goldReward = CalculateFinalGoldReward(); // 기존에 만든 골드 계산 함수
        if (GameFlowManager.Instance != null)
        {
            // 직접 대입하거나
            GameFlowManager.Instance.currentRunData.hp = (int)this.playerHP;

            // 또는 이미 만들어진 ApplyHPChange를 활용 (현재 체력으로 강제 설정)
            // GameFlowManager.Instance.ApplyHPChange(0); // 0을 더해서 현재 수치를 UI에 방송
        }
        // EconomyManager에서 랜덤 아이템 3개를 가져옴
        List<int> itemRewards = EconomyManager.Instance.DequeueGeneralRewards(3);

        // [중요] UIEffectManager_C에게 보상창을 띄우라고 명령
        // CombatManager에 선언된 Action 이벤트를 발생시킵니다.
        OnShowRewardPicker?.Invoke(itemRewards, goldReward);

        Debug.Log("<color=yellow>[Debug]</color> 모든 적 처치 완료 및 보상창 활성화.");
    }

    // 인벤토리 출력 로직 공용화
    private void PrintInventoryStatus()
    {
        if (economyManager == null) return;

        Debug.Log($"<color=white>────────── [인벤토리 현황 업데이트] ──────────</color>");

        if (economyManager.ownedItemIDs.Count == 0)
        {
            Debug.Log("<color=gray>보유한 아이템이 없습니다.</color>");
        }
        else
        {
            foreach (int id in economyManager.ownedItemIDs)
            {
                var data = economyManager.GetItemInfo(id);
                if (data != null)
                {
                    // [수정] 이름과 설명을 함께 출력하여 시인성 확보
                    Debug.Log($"<color=cyan>▶ [{data.id}: {data.name}]</color> : {data.description}");
                }
                else
                {
                    Debug.Log($"<color=red>▶ [{id}: 정보 없음]</color> CSV 데이터를 확인하세요.");
                }
            }
        }
    }

    // [v14.51] 몬스터 공격 모션 코루틴
    private IEnumerator PlayMonsterAttackMotion(GameObject monsterObj)
    {
        if (monsterObj == null) yield break;

        Vector3 originalPos = monsterObj.transform.position;
        // 몬스터는 오른쪽에 있고 왼쪽을 보므로, 앞으로 나가는 것은 -X 방향입니다.
        Vector3 forwardPos = originalPos + new Vector3(-0.4f, 0, 0); 

        float elapsed = 0f;
        float forwardDuration = 0.1f; // 빠르게 돌진
        float stayDuration = 0.1f;    // 타격 순간 멈춤
        float backDuration = 0.2f;   // 부드럽게 복귀

        // 1. 앞으로 돌진
        while (elapsed < forwardDuration)
        {
            monsterObj.transform.position = Vector3.Lerp(originalPos, forwardPos, elapsed / forwardDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        monsterObj.transform.position = forwardPos;

        // 2. 잠시 대기 (피격 연출과 동기화)
        yield return new WaitForSeconds(stayDuration);

        // 3. 원래 위치로 복귀
        elapsed = 0f;
        while (elapsed < backDuration)
        {
            monsterObj.transform.position = Vector3.Lerp(forwardPos, originalPos, elapsed / backDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        monsterObj.transform.position = originalPos;
    }
}

