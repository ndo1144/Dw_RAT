using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public enum BlockType { Attack, Defense, Special }
public class Block : MonoBehaviour
{
    public bool isMoving = false;
    public BlockType blockType; // 이 블록의 속성
    public int x;
    public int y;
    public int typeId;
    public bool isBomb = false; // [추가] 이 블록이 폭탄(수박)인지 여부
    public Match3Manager match3Manager;
    public bool isContaminated = false; // [v14.38] 쥐가 붙어 있는지 여부 (기존 로직 호환용)
    private GameObject stickyRatObject;  // [신규] 쥐 시각 효과 오브젝트
    //public Match3Manager1 match3Manager1;
    public void Setup(int x, int y)
    {
        this.x = x;
        this.y = y;
    }

    // [추가] 셔플 등으로 인해 블록 종류가 바뀔 때 호출
    public void RefreshVisual(int newTypeId, Sprite newSprite, BlockType newType)
    {
        this.typeId = newTypeId;
        this.blockType = newType;

        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr == null) sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null) sr.sprite = newSprite;
    }

    // [추가] 오염 상태 설정 및 시각적 연출
    public void SetContaminated(bool value)
    {
        isContaminated = value;
        // [v14.53] 보스 2페이즈일 때만 블록 색상 변경 연출 적용
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr == null) sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            bool isPhase2 = (match3Manager != null && match3Manager.bossPhase == 2);
            sr.color = (value && isPhase2) ? new Color(0.6f, 0.3f, 0.8f, 1f) : Color.white;
        }

        // 쥐가 제거될 때(false) 자식 오브젝트가 있다면 처리 (추락 애니메이션은 Manager에서 담당)
        if (!value && stickyRatObject != null)
        {
            // 여기서 직접 파괴하거나 추락 이벤트를 트리거할 수 있음
        }
    }

    public void AssignStickyRat(GameObject rat)
    {
        stickyRatObject = rat;
        isContaminated = true;
    }

    public GameObject GetStickyRat() => stickyRatObject;
    public void ClearStickyRat()
    {
        stickyRatObject = null;
        isContaminated = false;
    }
    // 외부에서 이동을 명령할 때 호출하는 함수
    public void MoveToPosition(Vector2 targetPos)
    {
        // 중요: 이미 이동 중이라면 중단시키고 새로 시작 (렉 방지)
        StopAllCoroutines();
        StartCoroutine(MoveCoroutine(targetPos));
    }

    // 실제로 부드럽게 이동시키는 코루틴
    private IEnumerator MoveCoroutine(Vector2 targetPos)
    {
        isMoving = true; // 시작할 때 true
        float duration = 0.15f; // 속도를 조금 더 빠르게
        float elapsedTime = 0f;
        Vector2 startPos = transform.position;

        while (elapsedTime < duration)
        {
            transform.position = Vector2.Lerp(startPos, targetPos, elapsedTime / duration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        // ★ 매우 중요: 이동 끝난 후 소수점 오차를 없애기 위해 목표값 그대로 대입
        transform.position = targetPos;
        isMoving = false; // 도착하면 false
    }
}

