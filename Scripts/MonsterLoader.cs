using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class MonsterData // CSV의 원본 데이터를 담는 틀
{
    public string id;
    public string type;
    public string name;
    public float hp;
    public float minHp;
    public float maxHp;
    public float atk;
    public float def;
    public int dropGold;
}

public class MonsterLoader : MonoBehaviour
{
    public TextAsset csvFile;
    public List<MonsterData> monsterTable = new List<MonsterData>();

    void Awake()
    {
        if (csvFile != null) LoadCSV();
    }
    // ID(문자열, 예: "N01")를 입력받아 해당하는 데이터를 찾아주는 함수
    public MonsterData GetMonsterByID(string id)
    {
        return monsterTable.Find(m => m.id == id);
    }
    public void LoadCSV()
    {
        monsterTable.Clear();
        string[] data = csvFile.text.Split(new string[] { "\r\n", "\n" }, System.StringSplitOptions.RemoveEmptyEntries);

        for (int i = 1; i < data.Length; i++)
        {
            string[] row = data[i].Split(',');
            if (row.Length < 6) continue;

            MonsterData monster = new MonsterData();
            // ID를 숫자로 변환하지 않고 그대로 문자열로 저장 (N01, B01 등 대응)
            monster.id = row[0].Trim();
            monster.type = row[1].Trim();
            monster.name = row[2].Trim(); // CSV 구조상 3번째 컬럼이 이름일 경우

            // HP가 "30~40"처럼 범위로 되어 있다면 최소/최대로 파싱하여 저장
            string[] hpSplit = row[3].Trim().Split('~');
            if (hpSplit.Length > 1)
            {
                monster.minHp = float.Parse(hpSplit[0]);
                monster.maxHp = float.Parse(hpSplit[1]);
            }
            else
            {
                monster.minHp = float.Parse(hpSplit[0]);
                monster.maxHp = monster.minHp;
            }
            monster.hp = monster.minHp; // 기존 코드 호환성용

            monster.atk = float.Parse(row[4].Trim());
            string goldvalue = row[5].Trim().Split('~')[0];
            monster.dropGold =int.Parse(goldvalue);

            monsterTable.Add(monster);
        }
        Debug.Log($"[MonsterLoader] 로드 완료: {monsterTable.Count}개");
    }
}