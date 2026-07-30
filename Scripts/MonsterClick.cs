using UnityEngine;

public class MonsterClick : MonoBehaviour
{
    public string monsterIDString;
    public float atkOverride = -1f; // -1이면 CSV 기본값 사용, 0 이상이면 이 값으로 덮어씀
    public CombatManager cbmanager;
    public CombatManager.MonsterInstance myInstance; // 이 쥐의 데이터
    private void Start()
    {
        // 시작 시 데이터 로드 시도
  
    }
   
    public void LinkInstance(CombatManager mgr, CombatManager.MonsterInstance inst)
    {
        cbmanager = mgr;
        myInstance = inst;
    }
    private void OnMouseDown()
    {
        // 헌법 수칙: Instance를 통한 직접 접근
        if (CombatManager.Instance != null && myInstance != null)
        {
            CombatManager.Instance.OnMonsterClicked(myInstance);
        }
    }
}
