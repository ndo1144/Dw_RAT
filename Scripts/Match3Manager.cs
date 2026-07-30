using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting.Antlr3.Runtime.Tree;
using UnityEngine;
using UnityEngine.EventSystems;
public struct TurnResult
{
    public int attack;   // 매치로 발생한 총 공격력
    public int defense;  // 매치로 발생한 총 방어력
    public int special;  // 특수 블록 등으로 발생한 수치
    public bool isFreeMove; // 턴 소모 없는 이동 여부

    public TurnResult(int a, int d, int s, bool free)
    {
        attack = a;
        defense = d;
        special = s;
        isFreeMove = free;
    }
}
public class Match3Manager : MonoBehaviour
{
    public static Match3Manager Instance { get; private set; }
    public int width = 7;
    public int height = 7;
    public GameObject[] BlockPrefabs;  // 일반 블록 프리팹 리스트
    public Block[,] PuzzleGrid;        // 보드 데이터 저장 배열
    public Vector2 startPos = new Vector2(-3f, -3f); // 보드 시작 좌표
    public Transform puzzleParent;     // 블록 생성 부모 객체
    //CombatManager
    public CombatManager combat;
    public GameObject bombBlockPrefab; // [복구] 701번 폭탄 전용 프리팹

    [Header("Boss Rat Gimmick (v14.38)")]
    public GameObject stickyRatPrefab; // [필수] BossRat 스크립트가 붙은 프리팹
    public int ratInvasionCount = 10;
    private bool isRatInvasionDone = false; 
    [Header("701 폭탄이쥐 연출")]
    public Sprite bombRatSprite;       // [v14.14] 폭탄 던지는 쥐 스프라이트 (인스펙터 할당)
    public Sprite bombProjectileSprite; // [v14.14] 날아가는 폭탄 스프라이트 (없으면 bombBlockPrefab 스프라이트 사용)
    public GameObject bombExplosionEffectPrefab; // [v14.42] 폭탄 폭발 이펙트 프리팹 (인스펙터 할당)
    public EconomyManager economyManager;
    // 내부 상태 변수
    private Block selectedBlock;       // 현재 선택된 첫 번째 블록
    private bool isProcessing = false; // 정산 중 입력 차단 플래그
    private int remainingBonusBombs = 0; // 보너스 폭발 횟수
    private int extraNormalBombs = 0;    // 이동 횟수 소모하는 일반 폭탄
    private int reservedBonusDamage = 0;   // 5매치 시 발생한 공격력 저장
    public int bombCount = 0;             // 현재 사용 가능한 총 폭탄 횟수 (1+2=3)
    private bool isSelectingBomb = false; // 폭탄이 될 블록을 선택 중인가?
    public bool isFree = false; // [추가] 턴 소모 여부를 저장할 변수
    private bool _isInteractable = true;
    // [v11.1] 800번대 아이템 관련 변수
    public bool isFreeDragActive = false; // 803번 활성화 여부
    private float freeDragTimer = 0f;      // 803번 남은 시간
    private bool isFreeDragMode = false;
    private Coroutine freeDragCoroutine; 
    private bool is110TriggeredThisBattle = false; // 전투당 1회 제한 플래그
    private bool is4MatchPendingThisTurn = false;  // 이번 턴(연쇄 포함) 중 4매치 발생 예약
    // 턴 결과 합산용
    private int totalAttack = 0;
    private int totalDefense = 0;
    private int totalSpecial = 0;
    private bool hasFiredFiveMatchThisTurn = false; // [702/703] 5매치 효과 트리거 추적용
    public int pendingBoardStrikes = 0;      // [v14.38] 예약된 보드판 폭행 횟수
    private bool isRatInvasionStarted = false; // [v14.46] 중복 호출 방지용
    public bool testFiveMatchMode = false;// 인스펙터에서 강제로 5매치 패턴을 테스트하기 위한 모드
    private List<Vector2> BlockToRemove = new List<Vector2>(); // 제거 예정 좌표 리스트
     public int bossPhase = 0; // [추가] 보스 페이즈 상태 (0: 일반, 1: 1페이즈, 2: 2페이즈)
    public int contaminationClearedInThisTurn = 0; // 이번 턴에 정화된 블록 수

    // [최적화] 외부(CombatManager)에서 조작 가능 여부를 명확히 주입
    public void SetInteractable(bool interactable)
    {
        _isInteractable = interactable;
        // 시각적 피드백이 필요하다면 여기에 보드 암전 이펙트 등을 추가 가능
    }
    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }
    void Start()
    {
        PuzzleGrid = new Block[width, height];
        InitBoard();// 게임 시작 시 보드 초기화

        // [v14.42] 보스전 스테이지인 경우 쥐 침공 시작
        if (GameFlowManager.Instance != null && GameFlowManager.Instance.currentStageType == StageType.Boss)
        {
            bossPhase = 1; // 1페이즈 강제 설정
            StartCoroutine(StartRatInvasion());
        }
    }
    // [확정 패턴] 플레이어가 한 번의 드래그로 5매치를 만들 수 있도록 강제 배치
    void InitBoard()
    {
        bool shouldApplyGimmick = testFiveMatchMode;
        if (GameFlowManager.Instance != null && GameFlowManager.Instance.currentRunData != null)
        {
            if (GameFlowManager.Instance.currentRunData.isFiveMatchGimmickPending)
            {
                shouldApplyGimmick = true;
            }
        }

        int centerX = width / 2; // 7칸 기준 3번 인덱스
        int targetY = 3;         // 목표 라인 (y=2): [A][A][B][A][A]
        int belowY = 2;          // 시작 라인 (y=1): 중앙에 [A] 배치

        int mainType = 0;        // 5매치 타겟 블록 (A)
        int avoidType = 1;       // 중앙 방해 블록 (B)

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (shouldApplyGimmick)
                {
                    // 1. 목표 라인 (y=2): [A] [A] [B] [A] [A] 배치
                    if (y == targetY)
                    {
                        if (x == centerX - 2 || x == centerX - 1 || x == centerX + 1 || x == centerX + 2)
                        {
                            SpawnBlockAt(x, y, mainType);
                            continue;
                        }
                        if (x == centerX)
                        {
                            SpawnBlockAt(x, y, avoidType);
                            continue;
                        }
                    }

                    // 2. 핵심 블록 (y=1): 중앙에 밀어 올릴 블록 [A] 배치
                    if (y == belowY && x == centerX)
                    {
                        SpawnBlockAt(x, y, mainType);
                        continue;
                    }
                }

                // 3. 일반 블록 생성 시 중복 방지 (v10.7.6 강화)
                if (PuzzleGrid[x, y] == null)
                {
                    List<int> possibleTypes = new List<int>();
                    for (int i = 0; i < BlockPrefabs.Length; i++) possibleTypes.Add(i);

                    // 헌법 준수: y=2줄의 A(mainType) 옆이나 위에 동일 블록 생성 금지
                    if (shouldApplyGimmick)
                    {
                        // 패턴 블록(mainType)과 인접한 경우 리스트에서 제거
                        if ((y == targetY && (x == centerX - 3 || x == centerX + 3)) || // A의 옆
                            (y == targetY + 1 && (x >= centerX - 2 && x <= centerX + 2 && x != centerX)) || // A의 위
                            (y == targetY - 1 && (x >= centerX - 2 && x <= centerX + 2 && x != centerX)))   // A의 아래
                        {
                            possibleTypes.Remove(mainType);
                        }
                    }

                    // 기존 SpawnBlockNoMatch 로직 통합 (3매치 자동 완성 방지)
                    int finalType = GetSafeRandomType(x, y, possibleTypes);
                    SpawnBlockAt(x, y, finalType);
                }
            }
        }

        // [v14.6.6] 초기 보드 생성 직후 교착상태(Deadlock) 체크 및 셔플
        if (!HasPossibleMoves())
        {
            StartCoroutine(ShuffleBoardRoutine());
        }

        // [v14.40] 기믹이 발동되었다면 1회성이므로 플래그 초기화
        if (shouldApplyGimmick && !testFiveMatchMode)
        {
            if (GameFlowManager.Instance != null && GameFlowManager.Instance.currentRunData != null)
            {
                GameFlowManager.Instance.currentRunData.isFiveMatchGimmickPending = false;
                Debug.Log("<color=yellow>[Match3]</color> 1회성 5매치 기믹이 발동되어 플래그를 초기화합니다.");
            }
        }
    }
    // 주변 블록을 체크하여 매칭되지 않는 타입을 반환하는 헬퍼 메서드
    private int GetSafeRandomType(int x, int y, List<int> possibleTypes)
    {
        List<int> validTypes = new List<int>(possibleTypes);

        if (x > 1 && PuzzleGrid[x - 1, y] != null && PuzzleGrid[x - 2, y] != null)
            if (PuzzleGrid[x - 1, y].typeId == PuzzleGrid[x - 2, y].typeId)
                validTypes.Remove(PuzzleGrid[x - 1, y].typeId);

        if (y > 1 && PuzzleGrid[x, y - 1] != null && PuzzleGrid[x, y - 2] != null)
            if (PuzzleGrid[x, y - 1].typeId == PuzzleGrid[x, y - 2].typeId)
                validTypes.Remove(PuzzleGrid[x, y - 1].typeId);

        return validTypes[Random.Range(0, validTypes.Count)];
    }
    // [메서드] 시작 시 3매치가 생성되지 않도록 랜덤하게 블록 스폰
    void SpawnBlockNoMatch(int x, int y)
    {
        List<int> validTypes = new List<int>();
        for (int i = 0; i < BlockPrefabs.Length; i++) validTypes.Add(i);

        // 왼쪽 체크 (x-1, x-2가 같으면 그 타입 제외)
        if (x >= 2 && PuzzleGrid[x - 1, y] != null && PuzzleGrid[x - 2, y] != null)
        {
            if (PuzzleGrid[x - 1, y].typeId == PuzzleGrid[x - 2, y].typeId)
                validTypes.Remove(PuzzleGrid[x - 1, y].typeId);
        }
        // 아래쪽 체크 (y-1, y-2가 같으면 그 타입 제외)
        if (y >= 2 && PuzzleGrid[x, y - 1] != null && PuzzleGrid[x, y - 2] != null)
        {
            if (PuzzleGrid[x, y - 1].typeId == PuzzleGrid[x, y - 2].typeId)
                validTypes.Remove(PuzzleGrid[x, y - 1].typeId);
        }

        // 오른쪽/위쪽은 아직 생성이 안 된 상태이므로 왼쪽과 아래만 체크하면 충분합니다.
        // 단, 패턴이 미리 배치되어 있다면 모든 방향을 체크하는 것이 안전합니다.

        int finalType = validTypes[Random.Range(0, validTypes.Count)];
        SpawnBlockAt(x, y, finalType);
    }
    // [최종 해결] 루트 스케일을 직접 제어하여 프리팹 크기에 관계없이 규격 통일
    // [최종 병기] 스프라이트의 실제 픽셀 크기를 무시하고 그리드 규격에 강제로 맞춤
    // [메서드] 특정 위치에 블록 생성 및 스케일 규격화
    void SpawnBlockAt(int x, int y, int typeId)
    {
        if (PuzzleGrid[x, y] != null) return;

        Vector3 worldPos = new Vector3(startPos.x + x, startPos.y + y, 0);
        GameObject go = Instantiate(BlockPrefabs[typeId], worldPos, Quaternion.identity, puzzleParent);

        go.name = $"Block_{x}_{y}";

        // 1. 루트 스케일 초기화 (부모 영향 배제)
        go.transform.localScale = Vector3.one;

        // 2. [핵심] SpriteRenderer의 Bounds를 계산하여 0.8f 크기로 강제 리사이징
        SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
        if (sr == null) sr = go.GetComponentInChildren<SpriteRenderer>(true);

        if (sr != null)
        {
            // 스프라이트의 현재 월드 크기를 가져옴
            float currentWidth = sr.bounds.size.x;
            if (currentWidth > 0)
            {
                // 1유닛(그리드 한 칸) 대비 0.8 비율이 되도록 역산하여 스케일 적용
                float targetScale = 1f / currentWidth;
                sr.transform.localScale = new Vector3(targetScale, targetScale, 1f);
            }

            // 3. 레이어 및 소팅 순서 강제 (다른 UI 뒤로 숨거나 앞으로 튀어나오는 것 방지)
            sr.sortingOrder = 10;
            sr.sortingLayerName = "Default"; // 프로젝트 설정에 맞는 레이어명으로 확인 필요
        }

        Block b = go.GetComponent<Block>();
        if (b == null) b = go.GetComponentInChildren<Block>();
        
        // [복구 보완] 프리팹에 Block 스크립트가 빠져있을 경우 강제 추가
        if (b == null)
        {
            Debug.LogError($"[경고] 프리팹(typeId: {typeId})에 Block 컴포넌트가 누락되어 강제로 AddComponent 합니다.");
            b = go.AddComponent<Block>();
        }

        if (b != null)
        {
            // [v14.48] 프리팹에 미리 붙어있을지 모르는 모든 쥐 오브젝트 청소 (중앙 출몰 방어)
            BossRat[] preRats = b.GetComponentsInChildren<BossRat>(true);
            foreach (var pr in preRats) Destroy(pr.gameObject);
            
            b.match3Manager = this; // 필수 참조
            b.typeId = typeId;
            b.Setup(x, y);
            PuzzleGrid[x, y] = b;
        }
    }
    void Update()
    {
        // 최적화: 단순 bool 체크로 프레임 부하 최소화
        if (!_isInteractable) return;


        if (isProcessing || isRatInvasionRunning) return; // [v14.77] 정산 또는 쥐 침공 연출 중 조작 불가
        //
        if (EventSystem.current.IsPointerOverGameObject()) return; // [헌법 4-4] UI 우선 원칙
        if (Input.GetMouseButtonDown(0)) 
        {
            // 적 턴이거나 UI 대기 중일 때 입력 완전 차단
            if (combat != null && (!combat.isPlayerTurn || combat.isWaitingForUI))
            {
                return;
            }
            // --- [701번 유료 폭탄 우선 처리 분기] ---
            if (isSelectingBomb && bombCount > 0)
            {
                HandleBombSelection(); // 폭탄 클릭 처리 전용 메서드
                return; // 일반 드래그 로직 실행 방지
            }
            // ---------------------------------------
            Debug.Log($"[클릭 체크] isProcessing 상태: {isProcessing}"); 
            HandleInputDown(); 
        }
        if (Input.GetMouseButtonUp(0)) 
        {
            // 위와 동일하게 차단 (스왑 완료 방지)
            if (combat != null && (!combat.isPlayerTurn || combat.isWaitingForUI))
            {
                return;
            }
            // 폭탄 선택 모드일 때는 Up 로직(스왑 완성) 무시
            if (isSelectingBomb) return;

            HandleInputUp();
        }
    }
    // Match3Manager.cs - HandleInputDown()
    // 2. [수정] 입력 처리 - 폭탄 선택 모드 우선 순위
    // [입력 처리] 마우스 클릭/터치 시작
    private void HandleInputDown()
    {
        if (isProcessing || isRatInvasionRunning) return; // [v14.77] 정산 또는 쥐 침공 중 입력 차단
        if (EventSystem.current.IsPointerOverGameObject()) return;

        // --- [교정] 턴이 바뀌어도 폭탄 선택이 우선되도록 보장 ---
        if (isSelectingBomb && bombCount > 0)
        {
            HandleBombSelection();
            return;
        }
        // ------------------------------------------

        if (!isFreeDragActive && combat != null && combat.moveCount >= 2) 
        {
            Debug.Log("[입력 거부] moveCount가 2 이상입니다.");
            return;
        }
        
        Vector3 mPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        int x = Mathf.RoundToInt(mPos.x - startPos.x);
        int y = Mathf.RoundToInt(mPos.y - startPos.y);

        // Debug.Log($"[InputDown] WorldPos: {mPos}, Grid X:{x}, Y:{y}");

        if (x >= 0 && x < width && y >= 0 && y < height)
        {
            Block clicked = PuzzleGrid[x, y];
            if (clicked == null) 
            {
                // ... (생략된 기존 로그)
                return;
            }

            // [v14.55] 쥐가 직접 붙은 블록만 조작 불가능 (2페이즈 단순 오염은 이동 가능)
            if (clicked.GetStickyRat() != null)
            {
                Debug.Log($"<color=orange>[Match3]</color> 쥐가 붙은 블록({x}, {y})은 움직일 수 없습니다!");
                return;
            }

            // [수정] bombCount > 0 조건 추가하여 소진된 폭탄 블록 재진입 방지
            if ((clicked.isBomb || clicked.typeId == 701) && bombCount > 0)
            {
                StartCoroutine(BombTransformationRoutine(clicked));
                return;
            }
            selectedBlock = clicked;
            // Debug.Log($"[InputDown] 선택된 블록: {selectedBlock.name}");
        }
        else
        {
            // Debug.LogWarning("[InputDown] 클릭 좌표가 그리드 범위를 벗어났습니다."); // 제거
        }
    }
    private void HandleInputUp()
    {
        if (selectedBlock == null) return;

        Vector3 mPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 diff = (Vector2)mPos - (Vector2)selectedBlock.transform.position;

        // Debug.Log($"[InputUp] Diff Magnitude: {diff.magnitude}");

            // 드래그 거리 판정 (0.3f 이상일 때 스왑 시도)
            if (diff.magnitude > 0.3f)
            {
                int originX = selectedBlock.x;
                int originY = selectedBlock.y;
                int tx = originX;
                int ty = originY;

                // 방향 판정
                if (Mathf.Abs(diff.x) > Mathf.Abs(diff.y)) tx += (diff.x > 0) ? 1 : -1;
                else ty += (diff.y > 0) ? 1 : -1;

                // 그리드 범위 체크
                if (tx >= 0 && tx < width && ty >= 0 && ty < height)
                {
                    Block targetBlock = PuzzleGrid[tx, ty];
                    if (targetBlock != null)
                    {
                        // [v14.55] 쥐가 붙은 블록으로의 스왑 차단
                        if (targetBlock.GetStickyRat() != null)
                        {
                            Debug.Log("<color=purple>[보스 기믹]</color> 쥐가 붙은 블록으로는 스왑할 수 없습니다!");
                            selectedBlock = null;
                            return;
                        }

                        // [v14.5.3] 코루틴을 통한 애니메이션 기반 스왑 시퀀스 시작
                        StartCoroutine(PerformSwapRoutine(originX, originY, tx, ty, isFreeDragActive));
                    }
            }
        }
        selectedBlock = null;
    }

    // [v14.5.3 신규] 애니메이션 가시성을 보장하는 스왑 및 원복 시퀀스
    private IEnumerator PerformSwapRoutine(int x1, int y1, int x2, int y2, bool isFreeDrag)
    {
        isProcessing = true; // 조작 잠금 시작

        Block b1 = PuzzleGrid[x1, y1];
        Block b2 = PuzzleGrid[x2, y2];

        // 1. 스왑 시도 (애니메이션 포함)
        SwapBlocks(x1, y1, x2, y2);
        
        // 블록 이동 애니메이션(0.15초)이 보일 때까지 대기
        yield return new WaitForSeconds(0.2f);

        // 2. 매칭 판정
        if (CheckMatches(true))
        {
            // [v14.38] 매칭 성공 시 주변 쥐 제거 (기존 정화 로직 대체)
            if (bossPhase == 1)
            {
                CheckAndRemoveNeighborRats(x1, y1);
                CheckAndRemoveNeighborRats(x2, y2);
            }

            // 매칭됨: 정산 루프 진입 (ExecuteProcessLoop가 내부에서 isProcessing을 최종 해제함)
            bool isFiveMatchItemActive = economyManager.ownedItemIDs.Contains(701) && isSelectingBomb;

            if (isFreeDrag)
            {
                StartCoroutine(ExecuteProcessLoop(b1, b2, true, false));
                Debug.Log("<color=purple>[803 자유드래그]</color> 매칭 정산 실행 (누적 중)");
            }
            else if (isFiveMatchItemActive)
            {
                StartCoroutine(ExecuteProcessLoop(b1, b2, false, false));
                Debug.Log("<color=cyan>[v14.1]</color> 5매치 감지! 수치 누적 후 폭탄 선택 대기.");
            }
            else
            {
                StartCoroutine(ExecuteProcessLoop(b1, b2, false, true));
            }
        }
        else
        {
            // 3. 매칭 실패: 가시적인 원복 애니메이션 수행 (UndoSwap 활용)
            Debug.Log("<color=yellow>[매칭 실패]</color> 원래 위치로 블록이 돌아갑니다. 오염은 유지됩니다.");
            UndoSwap(b1, b2, x1, y1, x2, y2);
            
            // 돌아오는 애니메이션 대기
            yield return new WaitForSeconds(0.2f);
            
            isProcessing = false; // 원복 완료 후 조작 잠금 해제

            // [v14.7.2] 뒤로 돌아온 상태에서도 매칭 가능한 수가 없는지 체크
            if (!HasPossibleMoves())
            {
                yield return StartCoroutine(ShuffleBoardRoutine());
            }
        }
    }

    // [규격 준수] SwapBlocks는 코루틴을 호출하지 않는 일반 메서드입니다.
    public void SwapBlocks(int col1, int row1, int col2, int row2)
    {
        // 유효성 검사
        if (col2 < 0 || col2 >= width || row2 < 0 || row2 >= height) return;

        Block b1 = PuzzleGrid[col1, row1];
        Block b2 = PuzzleGrid[col2, row2];
        // [v11.1 803] 자유드래그 모드일 때는 moveCount 제한을 면제
        if (b1 == null || b2 == null) return;

        // 1. 데이터 교체 (Grid 업데이트)
        PuzzleGrid[col1, row1] = b2;
        PuzzleGrid[col2, row2] = b1;

        // 2. Block 내부 정보 업데이트
        b1.Setup(col2, row2);
        b2.Setup(col1, row1);

        // 3. 시각적 위치 이동 (일반 메서드 내 호출)
        b1.MoveToPosition(new Vector2(startPos.x + col2, startPos.y + row2));
        b2.MoveToPosition(new Vector2(startPos.x + col1, startPos.y + row1));

        // [v14.5.5 변경] 매칭 실패 시 원복을 위해, 여기서 직접 정화하지 않고 PerformSwapRoutine에서 매칭 확인 후 정화함.
    }

    // [추가] 보스 기믹용 정화 로직
    private void CheckAndPurifyNeighbors(int x, int y)
    {
        int[] dx = { 0, 0, 1, -1 };
        int[] dy = { 1, -1, 0, 0 };

        for (int i = 0; i < 4; i++)
        {
            int nx = x + dx[i];
            int ny = y + dy[i];

            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
            {
                Block neighbor = PuzzleGrid[nx, ny];
                if (neighbor != null && neighbor.isContaminated)
                {
                    neighbor.SetContaminated(false);
                    contaminationClearedInThisTurn++;
                    Debug.Log($"<color=cyan>[정화]</color> ({nx}, {ny}) 블록이 정화되었습니다!");
                }
            }
        }
    }

    // [추가] 보스 기믹용 공용 메서드들
    public void TriggerRatInvasion(int count = 10)
    {
        ratInvasionCount = count;
        StartCoroutine(StartRatInvasion());
    }

    private bool isRatInvasionRunning = false; // 중복 실행 방지용 잠금

    public IEnumerator StartRatInvasion()
    {
        if (isRatInvasionRunning) yield break;
        isRatInvasionRunning = true;
        isProcessing = true; // [v14.76] 연출 중 입력 차단

        // [v14.53] 2페이즈에는 쥐들이 나오지 않음 (사용자 요청)
        if (bossPhase == 2)
        {
            // 2페이즈 전역 오염 처리 (아래에서 수행)
        }

        // [v14.48] 시작 전 씬에 혹시 남아있을지 모르는 모든 쥐 청소 (중앙 출몰 방지)
        BossRat[] existingRats = FindObjectsOfType<BossRat>();
        foreach (var r in existingRats) Destroy(r.gameObject);

        // 1. [탐색] 오염되지 않은 빈 블록들 수집 (중복 제거를 위해 HashSet 사용)
        HashSet<Block> uniqueCandidates = new HashSet<Block>();
        foreach (var b in PuzzleGrid)
        {
            if (b != null && !b.isContaminated && b.GetStickyRat() == null && !b.isBomb)
            {
                uniqueCandidates.Add(b);
            }
        }
        List<Block> candidates = new List<Block>(uniqueCandidates);
        System.Random rnd = new System.Random();
        candidates = candidates.OrderBy(a => rnd.Next()).ToList();

        // [v14.53] 2페이즈 분기: 쥐 생성 없이 '모든' 블록 즉시 오염 처리
        if (bossPhase == 2)
        {
            foreach (var target in candidates)
            {
                if (target != null) target.SetContaminated(true);
            }
            isRatInvasionDone = true;
            isRatInvasionRunning = false; 
            isProcessing = false; // [v14.76] 차단 해제
            yield break;
        }

        if (stickyRatPrefab == null)
        {
            isRatInvasionRunning = false;
            isProcessing = false;
            yield break;
        }

        int spawnCount = Mathf.Min(ratInvasionCount, candidates.Count);
        // --- 1페이즈 로직 (쥐 생성 및 점프) ---
        List<BossRat> spawnedRats = new List<BossRat>();
        float floorY = -3.6f; 
        float spawnFarX = 950.0f; 
        for (int i = 0; i < spawnCount; i++)
        {
            Vector3 farPos = new Vector3(spawnFarX, floorY+0.2f, -5f);
            GameObject ratObj = Instantiate(stickyRatPrefab, farPos, Quaternion.identity);
            
            ratObj.SetActive(false); 
            ratObj.transform.position = farPos; // 생성 즉시 격리
            
            BossRat br = ratObj.GetComponent<BossRat>();
            spawnedRats.Add(br);
            yield return new WaitForSeconds(0.01f); 
        }

        yield return new WaitForSeconds(0.3f); 

        // 2. [군집 연출] 바닥 전체 영역에 무작위로 배치 후 활성화
        for (int i = 0; i < spawnedRats.Count; i++)
        {
            // [v14.50] 보드판 영역 포함 전체 바닥 영역(-9 ~ 9.5)에 랜덤 배치
            float rx = UnityEngine.Random.Range(-9.0f, 9.5f);
            
            // [v14.50] 생성 높이를 조금 더 위쪽으로 조정 (-3.6 -> -2.8)
            Vector3 realPos = new Vector3(rx, -3.2f, -5f);
            
            spawnedRats[i].transform.position = realPos; 
            spawnedRats[i].gameObject.SetActive(true); 
            spawnedRats[i].PrepareJump(); 
        }
        
        if (AudioDirector.Instance != null) AudioDirector.Instance.PlaySqueak();
        yield return new WaitForSeconds(0.4f);

        // 3. [이동] 한꺼번에 점프하여 블록에 부착
        for (int i = 0; i < spawnCount; i++)
        {
            if (spawnedRats[i] != null && i < candidates.Count && candidates[i] != null)
            {
                // [v14.50] 점프 시작 즉시 해당 블록 예약 (중복 부착 방지)
                candidates[i].SetContaminated(true); 
                StartCoroutine(spawnedRats[i].JumpToBlock(candidates[i]));
                yield return new WaitForSeconds(0.06f); 
            }
        }

        // [v14.76] 쥐들이 점프를 완료할 때까지 약간 더 대기 (연출 완료 보장)
        yield return new WaitForSeconds(0.5f);

        isRatInvasionDone = true;
        isRatInvasionRunning = false;
        isProcessing = false; // [v14.76] 차단 해제
    }

    // [v14.48] 특정 블록(매치되어 사라진 블록) 주변 쥐를 떨어뜨리는 로직
    public void CheckAndRemoveNeighborRats(int x, int y)
    {
        int[] dx = { 0, 0, -1, 1 };
        int[] dy = { -1, 1, 0, 0 };

        for (int i = 0; i < 4; i++)
        {
            int nx = x + dx[i];
            int ny = y + dy[i];

            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
            {
                Block neighbor = PuzzleGrid[nx, ny];
                if (neighbor != null && neighbor.isContaminated)
                {
                    GameObject ratObj = neighbor.GetStickyRat();
                    if (ratObj != null)
                    {
                        BossRat br = ratObj.GetComponent<BossRat>();
                        if (br != null)
                        {
                            br.FallOff(); // [핵심] 실제 추락 코루틴 실행
                            Debug.Log($"<color=cyan>[쥐 추락]</color> ({nx}, {ny}) 블록에서 쥐가 떨어집니다!");
                        }
                    }
                    neighbor.ClearStickyRat(); // 상태 해제
                }
            }
        }
    }
    public void FinalEndingCheck()
    {
        int pollutedCount = 0;

        // 1. 현재 보드 위의 모든 블록을 순회하며 오염(쥐) 상태 확인
        // match3Manager가 관리하는 2차원 배열 혹은 리스트(예: board)를 사용하세요.
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Block b = PuzzleGrid[x, y];
                if (b != null && b.isContaminated)
                {
                    pollutedCount++;
                }
            }
        }

        Debug.Log($"[엔딩 판정] 남은 오염 블록: {pollutedCount}개");

        // 2. 판정에 따라 GameFlowManager를 통해 엔딩으로 이동
        if (pollutedCount == 0)
        {
            Debug.Log("진엔딩 확정!");
            GameFlowManager.Instance.nextEndingSceneName = "Scene_End2";
        }
        else
        {
            Debug.Log($"오염 {pollutedCount}개 남음. 일반엔딩 확정.");
            GameFlowManager.Instance.nextEndingSceneName = "Scene_End1";
        }
        GameFlowManager.Instance.EnterStage(StageType.Story);
    }
    public int GetContaminatedCount()
    {
        int count = 0;
        foreach (var b in PuzzleGrid)
            if (b != null && b.isContaminated) count++;
        return count;
    }

    public void ShakeCamera(float duration = 0.5f, float magnitude = 0.1f)
    {
        StartCoroutine(ShakeCoroutine(duration, magnitude));
    }

    private IEnumerator ShakeCoroutine(float duration, float magnitude)
    {
        Vector3 originalPos = Camera.main.transform.localPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float x = Random.Range(-1f, 1f) * magnitude;
            float y = Random.Range(-1f, 1f) * magnitude;

            Camera.main.transform.localPosition = new Vector3(x, y, originalPos.z);
            elapsed += Time.deltaTime;
            yield return null;
        }

        Camera.main.transform.localPosition = originalPos;
    }
    public bool CheckMatches(bool isDirectSwap = false)
    {
        bool hasMatch = false;
        BlockToRemove.Clear();

        // 1. 가로 매칭 검사
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int matchCount = 1;
                List<Vector2> currentLine = new List<Vector2> { new Vector2(x, y) };

                for (int i = x + 1; i < width; i++)
                {
                    if (PuzzleGrid[x, y] != null && PuzzleGrid[i, y] != null &&
                        PuzzleGrid[x, y].typeId == PuzzleGrid[i, y].typeId)
                    {
                        matchCount++;
                        currentLine.Add(new Vector2(i, y));
                    }
                    else break;
                }

                if (matchCount >= 3)
                {
                    hasMatch = true;
                    foreach (Vector2 pos in currentLine)
                    {
                        // [중요] BlockToRemove 리스트에 중복되지 않게 넣기
                        if (!BlockToRemove.Contains(pos))
                        {
                            BlockToRemove.Add(pos);
                        }
                        AddMatch(pos);
                    }

                    // [추가] 매칭 효과음 재생
                    if (AudioDirector.Instance != null) AudioDirector.Instance.PlayMatch(matchCount);

                    if (isDirectSwap && matchCount >= 5)
                    {
                        if (economyManager.ownedItemIDs.Contains(701))
                        {
                            bombCount = 3; // [수정] 3이 아닌 2로 설정 (클릭 2번에 종료)
                            isSelectingBomb = true;
                            Debug.Log($"<color=cyan>[데이터 예약]</color> 5매치 발생! 보너스 데미지 {reservedBonusDamage}점 예약 완료.");
                        }
                        else
                        {
                            foreach (Vector2 pos in currentLine)
                            {
                                // [중요] BlockToRemove 리스트에 중복되지 않게 넣기
                                if (!BlockToRemove.Contains(pos))
                                {
                                    BlockToRemove.Add(pos);
                                }
                                AddMatch(pos);
                            }
                            int matchedTypeId = PuzzleGrid[(int)currentLine[0].x, (int)currentLine[0].y]?.typeId ?? 0;
                            HandleFiveMatchItems(matchedTypeId);
                        }
                    }

                    x += matchCount - 1;
                }
            }
        }

        // 2. 세로 매칭 검사
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int matchCount = 1;
                List<Vector2> currentLine = new List<Vector2> { new Vector2(x, y) };

                for (int i = y + 1; i < height; i++)
                {
                    if (PuzzleGrid[x, y] != null && PuzzleGrid[x, i] != null &&
                        PuzzleGrid[x, y].typeId == PuzzleGrid[x, i].typeId)
                    {
                        matchCount++;
                        currentLine.Add(new Vector2(x, i));
                    }
                    else break;
                }

                if (matchCount >= 3)
                {
                    hasMatch = true;
                    foreach (Vector2 pos in currentLine)
                    {
                        // [중요] BlockToRemove 리스트에 중복되지 않게 넣기
                        if (!BlockToRemove.Contains(pos))
                        {
                            BlockToRemove.Add(pos);
                        }
                        AddMatch(pos);
                    }

                    // [추가] 매칭 효과음 재생
                    if (AudioDirector.Instance != null) AudioDirector.Instance.PlayMatch(matchCount);

                    if (isDirectSwap && matchCount >= 5)
                    {
                        if (economyManager.ownedItemIDs.Contains(701))
                        {

                            bombCount = 3;
                            isSelectingBomb = true;
                            Debug.Log($"<color=cyan>[데이터 예약]</color> 5매치 발생! 보너스 데미지 {reservedBonusDamage}점 예약 완료.");
                        }

                        else
                        {
                            hasMatch = true;
                            foreach (Vector2 pos in currentLine)
                            {
                                // [중요] BlockToRemove 리스트에 중복되지 않게 넣기
                                if (!BlockToRemove.Contains(pos))
                                {
                                    BlockToRemove.Add(pos);
                                }
                                AddMatch(pos);
                            }
                            int matchedTypeId = PuzzleGrid[(int)currentLine[0].x, (int)currentLine[0].y]?.typeId ?? 0;
                            HandleFiveMatchItems(matchedTypeId); // ← 이 한 줄 추가
                        }
                    }
                    y += matchCount - 1;
                }
            }
        }
        return hasMatch;
    }
    // [헬퍼 메서드] 배열 범위 및 타입 일치 여부 안전하게 확인
    bool IsMatch(int x1, int y1, int x2, int y2, int x3, int y3)
    {
        // 1. 배열 범위 이탈 방지 (IndexOutOfRangeException 원천 차단)
        if (x1 < 0 || x1 >= width || y1 < 0 || y1 >= height) return false;
        if (x2 < 0 || x2 >= width || y2 < 0 || y2 >= height) return false;
        if (x3 < 0 || x3 >= width || y3 < 0 || y3 >= height) return false;

        Block b1 = PuzzleGrid[x1, y1];
        Block b2 = PuzzleGrid[x2, y2];
        Block b3 = PuzzleGrid[x3, y3];

        // 2. Null 체크 및 타입 일치 확인
        if (b1 == null || b2 == null || b3 == null) return false;
        return (b1.typeId == b2.typeId && b2.typeId == b3.typeId);
    }

    // Match3Manager.cs의 MarkMatch 함수 수정
    // Match3Manager.cs 내부의 MarkMatch 함수를 아래 내용으로 교체
    void MarkMatch(int lastX, int lastY, int count, bool isHorizontal)
    {
        // [추가] 5매치 판정 및 아이템 효과 트리거
        if (count >= 5)
        {
            // 시작 지점의 블록을 참조하여 typeId 확인
            int startX = isHorizontal ? lastX - (count - 1) : lastX;
            int startY = isHorizontal ? lastY : lastY - (count - 1);

            if (PuzzleGrid[startX, startY] != null)
            {
                int matchedTypeId = PuzzleGrid[startX, startY].typeId;
                HandleFiveMatchItems(matchedTypeId);
            }
        }

        // 기존 블록 제거 리스트 추가 로직
        for (int i = 0; i < count; i++)
        {
            int x = isHorizontal ? lastX - i : lastX;
            int y = isHorizontal ? lastY : lastY - i;
            Vector2 pos = new Vector2(x, y);
            if (!BlockToRemove.Contains(pos)) BlockToRemove.Add(pos);
        }
    }
    void HandleFiveMatchSpecial(int typeId)
    {
        if (combat?.economyManager == null) return;
        // 이미 보드 생성 시 아이템은 소모되었으므로, 여기서는 효과만 발동
        combat.TriggerBoardDirectAttack();
    }
    // [새로 추가] 5매치 아이템 효과 처리 함수
    void HandleFiveMatchItems(int targetTypeId)
    {
        // [v14.38] 사용자 요청에 따라 한 턴 내 다중 발동 허용 (제한 제거)
        // [주의] hasFiredFiveMatchThisTurn은 이제 중복 방지가 아닌 '이번 턴 발동 여부' 기록용으로만 사용
        hasFiredFiveMatchThisTurn = true;
        
        // 701. 폭탄이 쥐: 5매치 시 폭탄 3개 획득 (선택 모드 진입)
        if (combat.economyManager.ownedItemIDs.Contains(701))
        {
            Debug.Log("<color=yellow>[701 폭탄이 쥐]</color> 5매치 발생! 폭탄 3개가 충전되었습니다.");
            bombCount += 3;
            isSelectingBomb = true;
        }

        // 702. 퍼즐보드 폭행: 보드판 직접 타격 (방어 무시)
        if (combat.economyManager.ownedItemIDs.Contains(702))
        {
            Debug.Log("<color=red>[702 퍼즐보드 폭행]</color> 5매치 발생! 안정화 후 타격 예약.");
            pendingBoardStrikes++; // [v14.38] 카운트 누적
        }
        // 703. 쥐돌이 기사단: 5매치 시 기사단 소환 (CombatManager에서 스택 처리)
        if (combat.economyManager.ownedItemIDs.Contains(703))
        {
            Debug.Log("<color=cyan>[703 쥐돌이 기사단]</color> 5매치 발생! 기사단 소환 트리거.");
            combat.AddRatKnightStack(3); // CombatManager의 currentRatKnightStack += 3
        }
    }
    void AddMatch(Vector2 pos)
    {
        if (!BlockToRemove.Contains(pos)) BlockToRemove.Add(pos);
    }

    void RemoveBlocks()
    {
        if (BlockToRemove.Count == 0) return;

        // 4매치 이상 골드 정산 예약 로직 (기존 유지)
        if (BlockToRemove.Count >= 4)
        {
            var economy = EconomyManager.Instance;
            if (economy != null && economy.ownedItemIDs != null && economy.ownedItemIDs.Contains(110))
            {
                if (!is110TriggeredThisBattle) is4MatchPendingThisTurn = true;
            }
        }

        // [v14.48] 추락할 쥐 오브젝트들을 중복 없이 수집하기 위한 셋
        HashSet<Block> blocksWithRatsToFall = new HashSet<Block>();

        // 1. 추락/정화 대상 블록 수집
        foreach (var pos in BlockToRemove)
        {
            int x = (int)pos.x; int y = (int)pos.y;
            if (PuzzleGrid[x, y] == null) continue;

            // [v14.56] 본인 블록: 쥐가 있든 단순 오염이든 무조건 정화 대상에 포함
            if (PuzzleGrid[x, y].isContaminated || PuzzleGrid[x, y].GetStickyRat() != null) 
                blocksWithRatsToFall.Add(PuzzleGrid[x, y]);

            // 인접 8방향 (대각선 포함) 블록 수집
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx;
                    int ny = y + dy;
                    if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                    {
                        Block neighbor = PuzzleGrid[nx, ny];
                        // [v14.56] 인접 블록 정화 조건 차별화:
                        // 쥐가 붙은 블록은 인접 매치 시 떨어지지만, 쥐 없는 단순 오염은 인접 매치로 정화되지 않음
                        if (neighbor != null && neighbor.GetStickyRat() != null) 
                        {
                            blocksWithRatsToFall.Add(neighbor);
                        }
                    }
                }
            }
        }

        // 2. 수집된 모든 쥐 일괄 추락 및 오염 정화 (v14.55 분리)
        foreach (var b in blocksWithRatsToFall)
        {
            GameObject ratObj = b.GetStickyRat();
            if (ratObj != null)
            {
                // 1. 쥐가 있다면 (1페이즈 방식): 쥐 추락
                BossRat br = ratObj.GetComponent<BossRat>();
                if (br != null) br.FallOff();
                b.ClearStickyRat();
                contaminationClearedInThisTurn++;
            }
            else if (b.isContaminated)
            {
                // 2. 쥐가 없고 오염만 되었다면 (2페이즈 방식): 즉시 정화 및 색상 복구
                b.SetContaminated(false);
                contaminationClearedInThisTurn++;
                Debug.Log("<color=green>[정화]</color> 오염된 블록이 정화되었습니다!");
            }
        }

        // 3. 실제 블록 제거 및 수치 합산
        foreach (var pos in BlockToRemove)
        {
            int x = (int)pos.x; int y = (int)pos.y;
            if (PuzzleGrid[x, y] != null)
            {
                BlockType t = PuzzleGrid[x, y].blockType;
                if (t == BlockType.Attack) totalAttack++;
                else if (t == BlockType.Defense) totalDefense++;
                else if (t == BlockType.Special) totalSpecial++;

                PuzzleGrid[x, y].gameObject.SetActive(false);
                PuzzleGrid[x, y] = null;
            }
        }
        BlockToRemove.Clear();
    }
    void DropBlocks()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (PuzzleGrid[x, y] == null)
                {
                    for (int ny = y + 1; ny < height; ny++)
                    {
                        if (PuzzleGrid[x, ny] != null)
                        {
                            PuzzleGrid[x, y] = PuzzleGrid[x, ny];
                            PuzzleGrid[x, ny] = null;
                            PuzzleGrid[x, y].Setup(x, y);
                            PuzzleGrid[x, y].MoveToPosition(new Vector2(startPos.x + x, startPos.y + y));
                            break;
                        }
                    }
                }
            }
        }

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (PuzzleGrid[x, y] == null)
                {
                    int rand = Random.Range(0, BlockPrefabs.Length);
                    Vector2 spawnPos = new Vector2(startPos.x + x, startPos.y + height + 2f);
                    GameObject go = Instantiate(BlockPrefabs[rand], spawnPos, Quaternion.identity);
                    if (puzzleParent != null) go.transform.SetParent(puzzleParent);
                    go.transform.localScale = Vector3.one;
                    SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
                    if (sr == null) sr = go.GetComponentInChildren<SpriteRenderer>(true);
                    if (sr != null)
                    {
                        float currentWidth = sr.bounds.size.x;
                        if (currentWidth > 0)
                        {
                            float targetScale = 1f / currentWidth;
                            sr.transform.localScale = new Vector3(targetScale, targetScale, 1f);
                        }
                        sr.sortingOrder = 10;
                        sr.sortingLayerName = "Default";
                    }

                    Block b = go.GetComponent<Block>();
                    b.match3Manager = this;
                    b.typeId = rand;
                    b.Setup(x, y);
                    PuzzleGrid[x, y] = b;
                    b.MoveToPosition(new Vector2(startPos.x + x, startPos.y + y));
                }
            }
        }
    }
    void UndoSwap(Block b1, Block b2, int c1, int r1, int c2, int r2)
    {
        PuzzleGrid[c1, r1] = b1; PuzzleGrid[c2, r2] = b2;
        b1.Setup(c1, r1); b2.Setup(c2, r2);
        b1.MoveToPosition(new Vector2(startPos.x + c1, startPos.y + r1));
        b2.MoveToPosition(new Vector2(startPos.x + c2, startPos.y + r2));
        // isProcessing 해제는 호출한 부모(코루틴)에서 애니메이션 대기 후 수행합니다.
    }
    bool AreBlocksMoving()
    {
        foreach (var b in PuzzleGrid) if (b != null && b.isMoving) return true;
        return false;
    }
    void PrintTurnResult()
    {
        List<string> results = new List<string>();
        //[수정] bool 변수 대신 수치가 0보다 큰지 직접 확인합니다.
        if (totalAttack > 0)
            results.Add($"공격: 기본 + {Mathf.Max(0, totalAttack - 3)}개");

        if (totalDefense > 0)
            results.Add($"방어: 기본 + {Mathf.Max(0, totalDefense - 3)}개");

        if (totalSpecial > 0)
            results.Add($"특수: 기본 + {Mathf.Max(0, totalSpecial - 3)}개");

        // 결과 출력
        if (results.Count > 0)
        {
            Debug.Log("<color=green><b>[턴 결과]</b></color> " + string.Join(", ", results));
        }
        else
        {
            // 아무것도 안 터졌을 때도 로그를 남겨서 작동 여부를 확인합니다.
            Debug.Log("<color=red>[턴 결과] 매칭된 블록이 없습니다.</color>");
        }

        Debug.Log($"<b>[이동횟수 종료 결과]</b> {{ {string.Join(", ", results)} }}");
    }
    // [v14.38] 기존 ExecuteFiveMatchGimmick은 HandleFiveMatchItems로 통합되었으므로 삭제 검토 (참조용으로 주석 처리하거나 제거)
    // void ExecuteFiveMatchGimmick(int itemId, int targetX, int targetY) { ... }
    // 플레이어가 선택한 블록을 폭탄으로 변환
    private IEnumerator BombTransformationRoutine(Block targetBlock)
    {
        isProcessing = true; // 보드 잠금
        isSelectingBomb = false; // 실행 중 중복 클릭 방지
        int tx = targetBlock.x;
        int ty = targetBlock.y;
        Vector3 spawnPos = targetBlock.transform.position;
        // 1. [v14.14] 쥐 등장 + 폭탄 투척 연출
        if (!targetBlock.isBomb)
        {
            Vector3 bombTargetPos = new Vector3(startPos.x + tx , startPos.y + ty, 0);
            yield return StartCoroutine(PlayBombRatAnimation(bombTargetPos));

            if (PuzzleGrid[tx, ty] != null)
                PuzzleGrid[tx, ty].gameObject.SetActive(false);

            GameObject bombObj = Instantiate(bombBlockPrefab, puzzleParent);
            bombObj.transform.position = new Vector3(startPos.x + tx, startPos.y + ty, 0);
            // [추가] 다른 블록과 동일한 크기로 정규화(SpawnBlockAt과 동일한 로직)
            bombObj.transform.localScale = Vector3.one;
            SpriteRenderer bombSr = bombObj.GetComponent<SpriteRenderer>();
            if (bombSr == null) bombSr = bombObj.GetComponentInChildren<SpriteRenderer>(true);
            if (bombSr != null)
            {
                float currentWidth = bombSr.bounds.size.x;
                if (currentWidth > 0)
                {
                    float targetScale = 1.0f / currentWidth; // [v14.14] 폭탄은 일반 블록보다 1.3배 크게
                    bombSr.transform.localScale = new Vector3(targetScale, targetScale, 1f);
                }
                bombSr.sortingOrder = 10;
                bombSr.sortingLayerName = "Default";
            }

            Block bombScript = bombObj.GetComponent<Block>();
            bombScript.Setup(tx, ty);
            bombScript.typeId = 701;
            bombScript.isBomb = true;
            PuzzleGrid[tx, ty] = bombScript;

            // 폭탄 변환 연출 대기
            yield return new WaitForSeconds(0.3f);
        }
        // [수정] ExecuteBombAt을 호출하지 않고 직접 폭발 로직 실행
        // (이중 isProcessing 충돌 방지)
        if (bombCount <= 0) { isProcessing = false; yield break; }

        int currentBombOrder = bombCount;
        bombCount--;
        bool isFreeForThisBomb;
        if (currentBombOrder == 3)
            isFreeForThisBomb = false; // 1번 폭탄: 5매치 수치와 묶어서 moveCount=1로 보고
        else
            isFreeForThisBomb = false; // 2번/3번 폭탄: 모두 유료 이동
        BlockToRemove.Clear();
        AddFlowerPattern(tx, ty);
        string label = currentBombOrder == 3 ? "1번(5매치 합산)" : currentBombOrder == 2 ? "2번(턴 종료 예정)" : "3번(새 턴 첫 이동)";
        Debug.Log($"<color=cyan>[701]</color> {label} 폭탄 발동. 남은 bombCount={bombCount}");

        // [v14.14] 폭탄 폭발 시 카메라 흔들림 및 이펙트 생성
        if (AudioDirector.Instance != null) AudioDirector.Instance.PlayBombExplosion();
        
        // [v14.44] 이펙트 생성 로직을 PlayBombRatAnimation 내부 착탄 시점으로 이동함

        ShakeCamera(0.3f, 0.08f);

        // [위임] ExecuteProcessLoop에서 정산 + finally에서 isSelectingBomb 복구
        // [v14.2] 마지막 폭탄 처리 시 moveCount 귀속 보장
        yield return StartCoroutine(ExecuteProcessLoop(null, null, false, true));
    }

    // ═══════════════════════════════════════════════════════════════
    // [v14.14] 폭탄이쥐 등장 + 포물선 투척 연출 코루틴
    // ═══════════════════════════════════════════════════════════════
    private IEnumerator PlayBombRatAnimation(Vector3 targetWorldPos)
    {
        // [v14.39] 스프라이트 미할당 시 자동 로드 폴백
        if (bombRatSprite == null)
        {
            bombRatSprite = Resources.Load<Sprite>("Icons/701");
        }
        if (bombProjectileSprite == null)
        {
            bombProjectileSprite = Resources.Load<Sprite>("Icons/701"); // 우선 동일 스프라이트 사용
        }

        if (bombRatSprite == null)
        {
            Debug.LogWarning("[701 연출] bombRatSprite를 찾을 수 없어 연출을 생략합니다.");
            yield break;
        }

        // ── 1. 쥐 오브젝트 생성 (화면 왼쪽 바깥) ──
        GameObject ratObj = new GameObject("BombRat_Anim");
        SpriteRenderer ratSr = ratObj.AddComponent<SpriteRenderer>();
        ratSr.sprite = bombRatSprite;
        ratSr.sortingLayerName = "Default";
        ratSr.sortingOrder = 100; // 모든 블록 위에 표시

        // 크기 정규화 (1.2유닛 높이 기준)
        float ratHeight = ratSr.bounds.size.y;
        if (ratHeight > 0)
        {
            float s = 1.2f / ratHeight;
            ratObj.transform.localScale = new Vector3(s, s, 1f);
        }

        // 시작 위치: 보드 왼쪽 바깥, 바닥 높이 (보드 영역 침범 방지 위해 거리 확장)
        float groundY = startPos.y ;
        Vector3 ratStart = new Vector3(startPos.x - 3.5f, groundY, 0f);
        // 멈출 위치: 보드 왼쪽 가장자리에서 충분히 떨어진 곳 (v14.41: 보드 침범 방지)
        Vector3 ratStop = new Vector3(startPos.x - 1.5f, groundY, 0f);
        ratObj.transform.position = ratStart;

        // [수정] 쥐 찍찍 소리를 등장 시작 시점으로 이동
        if (AudioDirector.Instance != null) AudioDirector.Instance.PlaySqueak();

        // ── 2. 쥐 등장 (슬라이드 인) ──
        float slideTime = 0.35f;
        float elapsed = 0f;
        while (elapsed < slideTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / slideTime);
            ratObj.transform.position = Vector3.Lerp(ratStart, ratStop, t);
            yield return null;
        }
        ratObj.transform.position = ratStop;

        // ── 3. 투척 준비 연출 (살짝 뒤로 + 흔들림) ──
        Vector3 windUp = ratStop + new Vector3(-0.15f, 0.05f, 0f);
        elapsed = 0f;
        float windUpTime = 0.15f;
        while (elapsed < windUpTime)
        {
            elapsed += Time.deltaTime;
            ratObj.transform.position = Vector3.Lerp(ratStop, windUp, elapsed / windUpTime);
            yield return null;
        }
        if (AudioDirector.Instance != null) AudioDirector.Instance.PlayBombThrow();

        // ── 4. 폭탄 발사체 생성 + 포물선 비행 ──
        GameObject projObj = new GameObject("BombProjectile");
        SpriteRenderer projSr = projObj.AddComponent<SpriteRenderer>();
        projSr.sortingLayerName = "Default";
        projSr.sortingOrder = 101;

        // 투사체 스프라이트 결정
        if (bombProjectileSprite != null)
        {
            projSr.sprite = bombProjectileSprite;
        }
        else if (bombBlockPrefab != null)
        {
            var bsr = bombBlockPrefab.GetComponent<SpriteRenderer>();
            if (bsr == null) bsr = bombBlockPrefab.GetComponentInChildren<SpriteRenderer>(true);
            if (bsr != null) projSr.sprite = bsr.sprite;
        }

        // 투사체 크기 정규화
        if (projSr.sprite != null)
        {
            float pw = projSr.bounds.size.x;
            if (pw > 0)
            {
                float ps = 0.6f / pw;
                projObj.transform.localScale = new Vector3(ps, ps, 1f);
            }
        }

        Vector3 throwStart = ratStop + new Vector3(0.3f, 0.4f, 0f);
        projObj.transform.position = throwStart;

        // [추가] 폭탄 투척 효과음 (조금 더 일찍)

        // 쥐를 원래 위치로 복귀 (투척 모션)
        ratObj.transform.position = ratStop;

        // 포물선 비행
        float flightTime = 0.4f;
        float arcHeight = 1.5f; // 포물선 최고점
        elapsed = 0f;
        while (elapsed < flightTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / flightTime);

            // 직선 보간 + 포물선 y 오프셋
            Vector3 linearPos = Vector3.Lerp(throwStart, targetWorldPos, t);
            float yArc = arcHeight * 4f * t * (1f - t); // 0→max→0
            projObj.transform.position = linearPos + new Vector3(0f, yArc, 0f);

            // 회전 연출 (빙글빙글)
            projObj.transform.Rotate(0f, 0f, -720f * Time.deltaTime);

            yield return null;
        }

        // ── 5. 착탄 시점 (v14.44: 가시적인 스케일 펀치 제거 후 프리팹에 위임) ──
        projObj.transform.position = targetWorldPos;
        projObj.transform.rotation = Quaternion.identity;

        // [v14.44] 실제 폭발 이펙트 생성 (이 시점이 가장 자연스러움)
        if (bombExplosionEffectPrefab != null)
        {
            GameObject effect = Instantiate(bombExplosionEffectPrefab, targetWorldPos, Quaternion.identity);
            
            var srs = effect.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var sr in srs) sr.sortingOrder = 200;

            var pss = effect.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in pss)
            {
                var psr = ps.GetComponent<ParticleSystemRenderer>();
                if (psr != null) psr.sortingOrder = 200;
            }

            Destroy(effect, 2.0f);
        }

        // 투사체는 즉시 제거 (이펙트가 대신 보여야 함)
        Destroy(projObj);



        // ── 6. 쥐 퇴장 (슬라이드 아웃) ──
        elapsed = 0f;
        float exitTime = 0.3f;
        while (elapsed < exitTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / exitTime);
            ratObj.transform.position = Vector3.Lerp(ratStop, ratStart, t);
            yield return null;
        }

        Destroy(ratObj);
    }

    private IEnumerator ProcessDelayedExplosion(int x, int y, bool isFree)
    {
        isProcessing = true;
        // isSelectingBomb = false; // 필요 시 주석 해제하여 사용

        // [연출] 폭탄으로 변한 모습을 보여주기 위해 대기
        yield return new WaitForSeconds(0.5f);

        // [폭발 범위 계산]
        // 첫 타격이 아니라면 기존 매치 목록을 비우고 꽃 모양 패턴만 적용
        if (remainingBonusBombs < 2) BlockToRemove.Clear();
        AddFlowerPattern(x, y);

        // 폭탄 중심 블록 확보
        Block bombBlock = PuzzleGrid[x, y];

        // [실행] ✅ (Block start, Block target, bool isFree) 형식을 엄격히 준수
        // 폭탄 발동이므로 bombBlock을 시작/대상으로 전달합니다.
        yield return StartCoroutine(ExecuteProcessLoop(bombBlock, bombBlock, isFree));

        // [헌법 5번] 모든 처리가 끝난 후 몬스터 사망 여부 확인 및 조작 차단
        // [핵심: 다회차 검증]
        if (bombCount > 0)
        {
            isSelectingBomb = true;  // 다시 선택 모드로
            isProcessing = false;    // 클릭 가능하게 해제
            Debug.Log($"<color=yellow>[701]</color> 폭탄 남음: {bombCount}. 다음 위치를 선택하세요.");
        }
        else
        {
            isSelectingBomb = false;
            isProcessing = false;
            // 모든 폭탄 소모 시에만 인터럽트 해제
            if (combat != null) combat.isWaitingForUI = false;
            Debug.Log("<color=green>[701]</color> 모든 폭탄 소모 완료.");
        }
    }
    private void TriggerDelayedExplosion(int x, int y)
    {
        // 1. 폭발 대상 블록 참조 확보
        Block bombBlock = PuzzleGrid[x, y];

        // 2. 기존 매치 데이터가 있다면 유지하되, 지연 폭발 전용 패턴 추가
        // (이미 AddFlowerPattern 내부에서 BlockToRemove.Clear()를 하므로 호출 순서 주의)
        AddFlowerPattern(x, y);

        // 3. [오류 해결] 코루틴 인수 전달 (폭탄 위치 블록을 start/target으로 전달)
        // 지연 폭발은 이미 이동이 끝난 후 발생하므로 isFree: true
        if (bombBlock != null)
        {
            StartCoroutine(ExecuteProcessLoop(bombBlock, bombBlock, true));
        }
    }
    // [헌법 4-1, 4-15 준수] 꽃 모양(Flower Pattern) 광역 폭발 시스템
    // 기존의 사각형 기반 메서드를 삭제하고 이 메서드로 통합합니다.
    public void AddFlowerPattern(int centerX, int centerY)
    {
        // [핵심] 눈꽃 모양 좌표 직접 정의 (중심 기준 상대 좌표)
        // 그림에 맞춰 터져야 하는 칸만 엄선했습니다.
        // 1. 8개 방향 정의 (상하좌우 + 대각선 4방향)
        Vector2Int[] directions = new Vector2Int[]
        {
            new Vector2Int(0,0),
        new Vector2Int(0, 1),   new Vector2Int(0, -1),  // 상, 하
        new Vector2Int(-1, 0),  new Vector2Int(1, 0),   // 좌, 우
        new Vector2Int(-1, 1),  new Vector2Int(1, 1),   // 좌상, 우상
        new Vector2Int(-1, -1), new Vector2Int(1, -1)  // 좌하, 우하
        };

        // 정의된 좌표들을 순회하며 제거 목록에 추가
        foreach (var offset in directions)
        {
            for (int i = 1; i <= 2; i++) // 1칸 거리, 2칸 거리 순차 추가
            {
                int tx = centerX + (offset.x * i);
                int ty = centerY + (offset.y * i);
                // [핵심] 보드 인덱스 범위(0 ~ width-1, 0 ~ height-1) 내에 있을 때만 처리
                if (tx >= 0 && tx < width && ty >= 0 && ty < height)
                {
                    Vector2 targetBlock =new Vector2( tx, ty);

                    // 블록이 존재하고, 아직 제거 목록에 없다면 추가
                    if (targetBlock != null && !BlockToRemove.Contains(targetBlock))
                    {
                        BlockToRemove.Add(targetBlock);
                    }
                }
            }
            
        }
        // 검증용 로그
        Debug.Log($"<color=yellow>[범위 계산 완료]</color> 중심:({centerX},{centerY}), 리스트 개수: {BlockToRemove.Count}");
    }
    // 별도 추출한 선택 로직
    // [교정] 클릭한 '그' 블록을 정확히 찾아 변환함
    // [수정] 폭탄 타겟 선택 및 중복 실행 방지
    private void HandleBombSelection()
    {
        if (isProcessing) return; // 이미 처리 중이면 리턴 (중복 로그 방지)
        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        // [보정] 특정 지점에 겹쳐있는 모든 콜라이더를 검사합니다.
        RaycastHit2D[] hits = Physics2D.RaycastAll(mousePos, Vector2.zero);
        foreach (var hit in hits)
        {
            Block clickedBlock = hit.collider.GetComponent<Block>();
            if (clickedBlock != null && clickedBlock.gameObject.activeInHierarchy)
            {
                if (PuzzleGrid[clickedBlock.x, clickedBlock.y] == clickedBlock)
                {
                    Debug.Log($"<color=cyan>[701]</color> 폭탄 변환 대상 확정: ({clickedBlock.x}, {clickedBlock.y})");
                    
                    // [추가] 폭탄 장전/선택 효과음
                    if (AudioDirector.Instance != null) AudioDirector.Instance.PlayBombReload();

                    // 선택 즉시 모드 해제 및 정산 루틴 진입
                    isSelectingBomb = false;
                    StartCoroutine(BombTransformationRoutine(clickedBlock));
                    return;
                }
            }
        }

        Debug.LogWarning("[701] 유효한 블록을 찾지 못했습니다. Collider 혹은 Z축을 확인하세요.");
    }
    // [메서드] 특정 좌표의 폭탄을 실행
    // Match3Manager.cs -> ExecuteBombAt 수정
    // Match3Manager.cs -> ExecuteBombAt 수정
    // [핵심] 폭탄 실행 메서드 수정
    // [v10.7.3] 1번 폭탄은 병합, 2/3번 폭탄은 개별 이동 횟수 카운트 적용

    private void ExecuteBombAt(int x, int y)
    {
        // [안전장치] 이미 bombCount가 0이거나 처리 중이면 즉시 종료
        if (isProcessing || bombCount <= 0) return;

        Block target = PuzzleGrid[x, y];
        if (target == null) return;

        // [핵심] 변환이 완료된 폭탄 블록을 그대로 BombTransformationRoutine에 넘김
        // Routine 내부에서 bombCount 감소, isFreeMove 판별, ExecuteProcessLoop 호출까지 일괄 처리
        StartCoroutine(BombTransformationRoutine(target));
    }
    // [코루틴] 블록 파괴-낙하-연쇄매칭의 전체 정산 루프
    private IEnumerator ExecuteProcessLoop(Block start, Block target, bool isFreeMove, bool reportsToCombat = true)
    {
        isProcessing = true; // 모든 입력 차단 시작
        is4MatchPendingThisTurn = false;

        try
        {
            // 1. 블록 매칭 및 연쇄 루프
            while (BlockToRemove.Count > 0)
            {
                yield return new WaitForSeconds(0.2f);
                RemoveBlocks(); // 4매치 시 폭탄 생성(bombCount++) 및 is4MatchPendingTrue

                yield return new WaitForSeconds(0.2f);
                DropBlocks();

                float timer = 0;
                while (AreBlocksMoving())
                {
                    timer += Time.deltaTime;
                    if (timer > 2f) break;
                    yield return null;
                }
                CheckMatches(false);
                yield return null;
            }

            // [v14.38] 모든 블록이 낙하하고 매칭 루프가 끝난 시점에 예약된 보드 폭행 실행
            while (pendingBoardStrikes > 0 && combat != null)
            {
                pendingBoardStrikes--;
                Debug.Log($"<color=red>[Match3]</color> 보드 안정화 완료. 예약된 폭행을 시작합니다. (잔여: {pendingBoardStrikes})");
                yield return StartCoroutine(combat.TripleBoardStrikeRoutine());

                // [사용자 요청] 폭행 후 기존 셔플 대신 '와르르' 쏟아지고 새로 채워지는 연출 실행
                yield return StartCoroutine(CrumbleAndRefillBoardRoutine());
            }

            // 2. [정산] 110번 골드 수급 및 전투 보고
            if (reportsToCombat && combat != null)
            {
                if (is4MatchPendingThisTurn)
                {
                    combat.ReserveItem110Effect();
                }

                TurnResult finalResult = new TurnResult(totalAttack, totalDefense, totalSpecial, isFreeMove);
                combat.OnMoveCompleted(finalResult);

                // [v14.37] 보스 스테이지인 경우에만 최종 정산 결과를 로그로 출력
                if (GameFlowManager.Instance != null && GameFlowManager.Instance.currentStageType == StageType.Boss)
                {
                    PrintTurnResult();
                }

                // 데이터 초기화 (헌법 준수)
                totalAttack = 0; totalDefense = 0; totalSpecial = 0;
                contaminationClearedInThisTurn = 0; // 정화 수치도 정산 시점에 비움
                is4MatchPendingThisTurn = false;
            }

            // [v14.6.6] 보드 안정화 후 교착상태(Deadlock) 체크 (전투 보고 여부와 관계없이 항상 수행)
            if (!HasPossibleMoves())
            {
                yield return StartCoroutine(ShuffleBoardRoutine());
            }
        }
        finally
        {
            // [헌법 4-45 준수] 정산 후 폭탄이 남았다면 반드시 플레이어에게 제어권을 돌려줌
            if (bombCount > 0)
            {
                isSelectingBomb = true;
                isProcessing = false; // ★ 핵심: 여기서 false가 되어야 HandleInputDown이 동작함
                Debug.Log($"<color=yellow>[System]</color> 정산 완료. 폭탄 남음({bombCount}). 조작 잠금 해제.");
            }
            else
            {
                isSelectingBomb = false;
                isProcessing = false;
                // 폭탄 종료 시 UI 대기 해제
                if (combat != null) combat.isWaitingForUI = false;
            }
        }
    }
    // 803번 전용 실행 메서드
    public void ExecuteItem803()
    {
        if (isProcessing || isFreeDragActive) return;

        // 안전장치: 기존 코루틴이 있다면 확실히 정지하고 변수 초기화
        if (freeDragCoroutine != null)
        {
            StopCoroutine(freeDragCoroutine);
            freeDragCoroutine = null;
        }

        isFreeDragActive = true;
        freeDragCoroutine = StartCoroutine(FreeDragRoutine(10f));
    }
    private IEnumerator FreeDragRoutine(float duration)
    {
        isFreeDragActive = true;
        // 803 모드 중 누적 수치 초기화 (이전 턴 잔재 방지)
        totalAttack = 0;
        totalDefense = 0;
        totalSpecial = 0;
        hasFiredFiveMatchThisTurn = false;

        Debug.Log("<color=purple>[803] 자유 드래그 모드 진입! 10초간 자유롭게 매칭하세요.</color>");
        float timer = duration;
        // yield return null을 통해 프레임 대기를 확실히 함
        while (timer > 0f)
        {
            timer -= Time.deltaTime;
            // 타이머 수치를 멤버 변수에 동기화 (UI용)
            freeDragTimer = timer;
            yield return null;
        }

        // 타이머 종료 후 정산
        isFreeDragActive = false;
        freeDragCoroutine = null;
        Debug.Log("<color=gray>[803] 자유 드래그 모드 종료.</color>");
        float waitTimer = 0f;
        while (isProcessing && waitTimer < 3f)
        {
            waitTimer += Time.deltaTime;
            yield return null;
        }

        ProcessFinalMatch803();
    }

    // [v14.39] 701번 폭탄이쥐 액티브 발동 API
    public void ActivateBombRat(int count)
    {
        if (isProcessing || isSelectingBomb) return;

        bombCount = count;
        isSelectingBomb = true;
        isProcessing = false; // 플레이어가 선택할 수 있도록 해제
        
        // 전투 중이라면 UI 대기 해제하여 클릭 유도
        if (combat != null) combat.isWaitingForUI = false;

        Debug.Log($"<color=yellow>[701]</color> 폭탄이쥐 액티브 발동! 폭탄 {count}개가 준비되었습니다.");
    }

    // Match3Manager.cs
    // ProcessFinalMatch803 전면 교체
    private void ProcessFinalMatch803()
    {
        if (combat == null) return;
        // ProcessFinalMatch803 내부 첫 줄
        Debug.Log($"<color=red>[803 최종 원시값]</color> attack={totalAttack}, defense={totalDefense}, special={totalSpecial}");
        // [핵심] 누적된 블록 수치를 그대로 TurnResult로 포장
        // OnMoveCompleted 내부에서 calcAtk/calcDef/CalculateFinalDamage가 정상 적용됨
        // isFreeMove = false → moveCount++ 및 savedResults.Add() 정상 실행
        TurnResult result803 = new TurnResult(totalAttack, totalDefense, totalSpecial, false);

        // [헌법 3번 API] OnMoveCompleted를 통한 단일 경로 보고
        // moveCount가 0인 상태에서 1회 호출 → moveCount=1이 됨
        // 이후 선택창은 moveCount >= 2가 되어야 뜨므로,
        // 803은 moveCount를 1로만 올리고 선택창 없이 바로 Inject803Result로 주입
        combat.Inject803Result(result803);

        // 누적 수치 초기화
        totalAttack = 0;
        totalDefense = 0;
        totalSpecial = 0;
    }
    public void ExecuteItem805()
    {
        if (combat != null)
        {
            // 현재 소모된 moveCount를 1회 감소시켜 턴을 되돌림 (0 이하 방지)
            combat.moveCount = Mathf.Max(0, combat.moveCount - 1);

            // UI 업데이트 (CombatManager에 moveText 업데이트 함수가 있다면 호출)
            combat.UpdateMoveText();
            Debug.Log("<color=cyan>[805] 시간을 되돌렸습니다. 턴 1회 복구!</color>");
        }
    }
    public void EnableFreeDrag(float duration)
    {
        StartCoroutine(FreeDragRoutine(duration));
    }
    public void ResumePuzzle()
    {
        isProcessing = false;
        hasFiredFiveMatchThisTurn = false; // [v14.38] 턴 재개 시 플래그 초기화
        pendingBoardStrikes = 0; // 안전장치: 턴 시작 시 잔여 예약 초기화
    }
    // Match3Manager에 추가
    public void ResumeBombMode()
    {
        if (bombCount > 0)
        {
            // 핵심: 이전 턴의 정산 상태를 완전히 초기화
            isProcessing = false;
            isSelectingBomb = true;

            // 시각적 피드백 (선택 사항: UI 매니저를 통해 '폭탄 선택 필요' 알림)
            Debug.Log($"<color=orange>[701]</color> 폭탄 모드 재개. 남은 폭탄: {bombCount}. 블록을 선택하세요.");
            // [핵심 수정] isWaitingForUI = false로 변경
            // true이면 Update()에서 클릭 자체가 차단되어 HandleBombSelection()에 도달 불가
            if (combat != null) combat.isWaitingForUI = false;
        }
    }
    /// <summary>
    /// 보드판에 매칭 가능한 수(Move)가 하나라도 있는지 체크합니다.
    /// </summary>
    public bool HasPossibleMoves()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Block b = PuzzleGrid[x, y];
                if (b == null) continue;
                
                // [v14.7.2] 폭탄이 하나라도 있으면 교착 상태가 아님 (사용자가 행동 가능하므로)
                if (b.isBomb || b.typeId == 701) return true;

                // 1. 우측 블록과 스왑 시뮬레이션
                if (x < width - 1)
                {
                    Block right = PuzzleGrid[x + 1, y];
                    // [v14.12.6] 1페이즈일 때만 오염 블록 스왑 제한 적용
                    bool isContaminatedBlocked = (bossPhase == 1 && (b.isContaminated || right.isContaminated));
                    if (right != null && !right.isBomb && !isContaminatedBlocked)
                    {
                        if (SimulateMatch(x, y, x + 1, y)) return true;
                    }
                }

                // 2. 하단 블록과 스왑 시뮬레이션
                if (y < height - 1)
                {
                    Block down = PuzzleGrid[x, y + 1];
                    // [v14.12.6] 1페이즈일 때만 오염 블록 스왑 제한 적용
                    bool isContaminatedBlocked = (bossPhase == 1 && (b.isContaminated || down.isContaminated));
                    if (down != null && !down.isBomb && !isContaminatedBlocked)
                    {
                        if (SimulateMatch(x, y, x, y + 1)) return true;
                    }
                }
            }
        }
        return false;
    }


    private bool SimulateMatch(int x1, int y1, int x2, int y2)
    {
        // [v14.7.3] 가상 스왑 시뮬레이션: 참조 교체 방식 도입
        // 단순히 옆의 타입만 체크하면, 스왑 시 사라지는 블록을 고려하지 못함.
        
        Block b1 = PuzzleGrid[x1, y1];
        Block b2 = PuzzleGrid[x2, y2];

        if (b1 == null || b2 == null) return false;

        // 가상으로 위치 교체
        PuzzleGrid[x1, y1] = b2;
        PuzzleGrid[x2, y2] = b1;

        // 바뀐 위치에서 매칭 발생 여부 정밀 체크
        bool match = IsMatchAt(x1, y1, b2.typeId) || IsMatchAt(x2, y2, b1.typeId);

        // 원복
        PuzzleGrid[x1, y1] = b1;
        PuzzleGrid[x2, y2] = b2;

        return match;
    }

    private bool IsMatchAt(int x, int y, int typeId)
    {
        // 세로 체크
        int verticalCount = 1;
        // 위로
        for (int i = y + 1; i < height; i++)
        {
            if (PuzzleGrid[x, i] != null && PuzzleGrid[x, i].typeId == typeId) verticalCount++;
            else break;
        }
        // 아래로
        for (int i = y - 1; i >= 0; i--)
        {
            if (PuzzleGrid[x, i] != null && PuzzleGrid[x, i].typeId == typeId) verticalCount++;
            else break;
        }
        if (verticalCount >= 3) return true;

        // 가로 체크
        int horizontalCount = 1;
        // 오른쪽
        for (int i = x + 1; i < width; i++)
        {
            if (PuzzleGrid[i, y] != null && PuzzleGrid[i, y].typeId == typeId) horizontalCount++;
            else break;
        }
        // 왼쪽
        for (int i = x - 1; i >= 0; i--)
        {
            if (PuzzleGrid[i, y] != null && PuzzleGrid[i, y].typeId == typeId) horizontalCount++;
            else break;
        }
        if (horizontalCount >= 3) return true;

        return false;
    }

    /// <summary>
    /// 교착 상태 시 보드를 무작위로 재배치하는 루틴 (forceShuffle: 폭행 등 특수 상황 시 강제 실행)
    /// </summary>
    private IEnumerator ShuffleBoardRoutine(bool forceShuffle = false)
    {
        isProcessing = true;
        
        if (forceShuffle)
            Debug.Log("<color=red><b>[Shuffle]</b></color> 퍼즐보드 폭행 효과로 보드를 강제로 섞습니다. (오염 위치 보존)");
        else
            Debug.Log("<color=orange><b>[Shuffle]</b></color> 매칭 가능한 수가 없습니다! 보드를 섞습니다.");

        // [연출] 잠시 대기
        yield return new WaitForSeconds(0.5f);

        bool success = false;
        int maxAttempts = 20;
        int attempt = 0;

        while (!success && attempt < maxAttempts)
        {
            attempt++;
            
            // 보드 전체 타입 재할당 (3매칭 방지 로직 포함)
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (PuzzleGrid[x, y] != null && !PuzzleGrid[x, y].isBomb)
                    {
                        List<int> possible = new List<int>();
                        for (int i = 0; i < BlockPrefabs.Length; i++) possible.Add(i);

                        int newType = GetSafeRandomType(x, y, possible);
                        
                        // 프리팹에서 정보 추출
                        GameObject prefab = BlockPrefabs[newType];
                        SpriteRenderer psr = prefab.GetComponent<SpriteRenderer>();
                        if (psr == null) psr = prefab.GetComponentInChildren<SpriteRenderer>();
                        Block pb = prefab.GetComponent<Block>();
                        if (pb == null) pb = prefab.GetComponentInChildren<Block>();

                        if (psr != null && pb != null)
                        {
                            PuzzleGrid[x, y].RefreshVisual(newType, psr.sprite, pb.blockType);
                        }
                    }
                }
            }

            // 셔플 후 매칭 가능한 수가 생겼는지 확인 (강제 셔플인 경우에도 최소 1개 이상의 무브 보장)
            if (HasPossibleMoves()) success = true;
        }

        Debug.Log($"<color=green><b>[Shuffle]</b></color> 보드 재배치 완료 (시도 횟수: {attempt})");
        yield return new WaitForSeconds(0.5f);
        
        // [v14.1] 폭탄 모드 등이 아니면 잠금 해제
        if (bombCount <= 0)
        {
            isProcessing = false;
        }
    }

    /// <summary>
    /// [v14.66] 보드판 폭행 시 블록들이 아래로 쏟아지고 천장에서 새 블록이 내려오는 연출
    /// </summary>
    private IEnumerator CrumbleAndRefillBoardRoutine()
    {
        isProcessing = true;
        Debug.Log("<color=red><b>[Board Crumble]</b></color> 블록들이 와르르 무너져 내립니다!");

        // 1. 모든 기존 블록 폭죽 불똥처럼 날리기 (포물선 산란)
        List<Block> allBlocks = new List<Block>();
        Vector2 center = new Vector2(startPos.x + (width - 1) / 2f, startPos.y + (height - 1) / 2f);

        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = 0; x < width; x++)
            {
                if (PuzzleGrid[x, y] != null)
                {
                    allBlocks.Add(PuzzleGrid[x, y]);
                    PuzzleGrid[x, y] = null;
                }
            }
        }

        // [v14.77] 폭죽이 터지듯 중앙에서 사방으로 블록 날리기
        foreach (var b in allBlocks)
        {
            if (b == null) continue;
            
            // 중앙 기준 방향 계산
            Vector2 pos = b.transform.position;
            Vector2 dir = (pos - center).normalized;
            if (dir == Vector2.zero) dir = UnityEngine.Random.insideUnitCircle.normalized;

            // [v14.79] 강력한 중앙 폭발 연출 (폭탄이 터지듯 순식간에 산란)
            StartCoroutine(FireworkArcRoutine(b, dir, Vector2.Distance(pos, center)));
            
            // 폭발의 동시성을 위해 초미세 지연 시간만 부여 (0.001~0.003초)
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.001f, 0.005f));
        }

        // 모든 연출이 충분히 진행될 때까지 대기
        yield return new WaitForSeconds(1.0f);

        // 2. 쏟아진 오브젝트들 실제 파괴
        foreach (var b in allBlocks)
        {
            if (b != null) Destroy(b.gameObject);
        }
        allBlocks.Clear();

        yield return new WaitForSeconds(0.2f);

        // 3. 천장에서 새로운 블록들이 비오듯 쏟아지며 채우기
        // (한 줄씩 순차적으로 생성하여 더욱 역동적인 느낌 부여)
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // 중복 방지 랜덤 타입 결정
                List<int> possible = new List<int>();
                for (int i = 0; i < BlockPrefabs.Length; i++) possible.Add(i);
                int typeId = GetSafeRandomType(x, y, possible);

                // 스폰 지점: 보드 위쪽 (height + 2~5 정도)
                Vector3 spawnPos = new Vector3(startPos.x + x, startPos.y + height + 2f, 0);
                GameObject go = Instantiate(BlockPrefabs[typeId], spawnPos, Quaternion.identity, puzzleParent);
                
                // [v14.6.4 규격 준수] 리사이징 및 초기화
                go.transform.localScale = Vector3.one;
                SpriteRenderer sr = go.GetComponentInChildren<SpriteRenderer>();
                if (sr != null)
                {
                    float currentWidth = sr.bounds.size.x;
                    if (currentWidth > 0)
                    {
                        float targetScale = 1f / currentWidth;
                        sr.transform.localScale = new Vector3(targetScale, targetScale, 1f);
                    }
                    sr.sortingOrder = 10;
                }

                Block b = go.GetComponent<Block>();
                if (b == null) b = go.AddComponent<Block>();
                b.match3Manager = this;
                b.typeId = typeId;
                b.Setup(x, y);
                PuzzleGrid[x, y] = b;

                // 목표 위치로 낙하 (MoveToPosition 사용)
                b.MoveToPosition(new Vector2(startPos.x + x, startPos.y + y));
            }
            // 줄 간의 미세한 딜레이로 쏟아지는 연출 강화
            yield return new WaitForSeconds(0.06f);
        }

        // 모든 블록이 안착할 때까지 대기
        float timeout = 2.0f;
        while (AreBlocksMoving() && timeout > 0)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        // 4. 교착 상태 최종 체크
        if (!HasPossibleMoves())
        {
            yield return StartCoroutine(ShuffleBoardRoutine());
        }

        isProcessing = false;
        Debug.Log("<color=green><b>[Board Crumble]</b></color> 보드 재생성 및 안착 완료.");
    }

    // [v14.83] 폭탄 폭발 + 포물선(투사체) 물리 시뮬레이션
    private IEnumerator FireworkArcRoutine(Block b, Vector2 dir, float distFromCenter)
    {
        if (b == null) yield break;

        // 중앙에서 가까울수록 더 강하게 밀려남
        float powerMult = 1.0f + (1.5f / (distFromCenter + 0.5f));
        float force = UnityEngine.Random.Range(6f, 10f) * powerMult;

        // 초기 폭발 속도: 중앙→바깥 방향 + 약간의 랜덤 흔들림
        Vector2 velocity = dir * force + UnityEngine.Random.insideUnitCircle * 1.5f;
        // 위쪽으로 약간의 초기 상승력 추가 (폭발이 살짝 솟구치는 느낌)
        velocity.y += UnityEngine.Random.Range(2f, 5f);

        float gravity = 25f; // 중력 가속도
        float rotationSpeed = UnityEngine.Random.Range(-360f, 360f); // 회전 속도 (도/초)

        // 매 프레임 물리 시뮬레이션 (진짜 포물선 운동)
        while (b != null && b.transform.position.y > -15f)
        {
            float dt = Time.deltaTime;

            // 중력 적용
            velocity.y -= gravity * dt;

            // 위치 갱신
            b.transform.position += (Vector3)(velocity * dt);

            // 회전 적용 (날아가면서 빙글빙글 도는 느낌)
            b.transform.Rotate(0, 0, rotationSpeed * dt);

            yield return null;
        }

        // 화면 밖으로 나간 블록 즉시 비활성화
        if (b != null) b.gameObject.SetActive(false);
    }

    // [v14.85] 타격 충격으로 블록 일부를 떨어뜨리는 루틴
    public IEnumerator ShakeOffBlocksRoutine(int count)
    {
        // 현재 보드에 남아있는 블록 수집
        List<Block> remaining = new List<Block>();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (PuzzleGrid[x, y] != null)
                    remaining.Add(PuzzleGrid[x, y]);

        // 셔플하여 랜덤 선택
        for (int i = remaining.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            var temp = remaining[i];
            remaining[i] = remaining[j];
            remaining[j] = temp;
        }

        int toDrop = Mathf.Min(count, remaining.Count);
        Vector2 center = new Vector2(startPos.x + (width - 1) / 2f, startPos.y + (height - 1) / 2f);

        for (int i = 0; i < toDrop; i++)
        {
            Block b = remaining[i];
            if (b == null) continue;

            // 그리드에서 제거
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (PuzzleGrid[x, y] == b)
                        PuzzleGrid[x, y] = null;

            // 물리 시뮬레이션으로 떨어뜨리기 (아래 방향 + 약간의 좌우)
            Vector2 dir = ((Vector2)b.transform.position - center).normalized;
            if (dir == Vector2.zero) dir = Vector2.down;
            // 아래 방향 강조
            dir.y = Mathf.Min(dir.y, -0.3f);
            dir = dir.normalized;

            StartCoroutine(FireworkArcRoutine(b, dir, 1f));
        }

        // 떨어지는 연출 약간 대기
        yield return new WaitForSeconds(0.3f);
    }
}
