import requests
import csv
import json
import os
import opencc

# 國服資料來源
CN_ACTION_CSV_URL = "https://raw.githubusercontent.com/thewakingsands/ffxiv-datamining-cn/refs/heads/master/Action.csv"
# 輸出檔案名稱 (存放在 Repo 根目錄)
OUTPUT_FILENAME = "actions_zhtw.json"

def main():
    print(f"Downloading CSV from {CN_ACTION_CSV_URL}...")

    try:
        response = requests.get(CN_ACTION_CSV_URL)
        response.raise_for_status()
    except Exception as e:
        print(f"Failed to download: {e}")
        exit(1) # CI 需要知道失敗了

    decoded_content = response.content.decode('utf-8')
    csv_reader = csv.reader(decoded_content.splitlines())

    # 設定 OpenCC (簡體 -> 臺灣正體)
    converter = opencc.OpenCC('s2twp.json')

    action_map = {}

    for row in csv_reader:
        if len(row) < 2 or not row[0].isdigit():
            continue

        action_id = row[0]
        action_name_sc = row[1]

        if not action_name_sc:
            continue

        # 轉換並存入
        action_name_tc = converter.convert(action_name_sc)
        action_map[action_id] = action_name_tc

    print(f"Processed {len(action_map)} actions.")

    # 寫入 JSON
    with open(OUTPUT_FILENAME, 'w', encoding='utf-8') as f:
        json.dump(action_map, f, ensure_ascii=False, separators=(',', ':'))

    print(f"Successfully generated {OUTPUT_FILENAME}")

if __name__ == "__main__":
    main()
