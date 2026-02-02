import requests
import csv
import json
import opencc

# Chinese server data source
CN_ACTION_CSV_URL = "https://raw.githubusercontent.com/thewakingsands/ffxiv-datamining-cn/refs/heads/master/Action.csv"
# Output filename (stored in repo root)
OUTPUT_FILENAME = "actions_zhtw.json"


def main():
    print(f"Downloading CSV from {CN_ACTION_CSV_URL}...")

    try:
        response = requests.get(CN_ACTION_CSV_URL)
        response.raise_for_status()
    except Exception as e:
        print(f"Failed to download: {e}")
        exit(1)  # CI needs to know about failures

    decoded_content = response.content.decode('utf-8')
    csv_reader = csv.reader(decoded_content.splitlines())

    # Configure OpenCC (Simplified Chinese -> Taiwan Traditional)
    converter = opencc.OpenCC('s2twp.json')

    action_map = {}

    for row in csv_reader:
        if len(row) < 2 or not row[0].isdigit():
            continue

        action_id = row[0]
        action_name_sc = row[1]

        if not action_name_sc:
            continue

        # Convert and store
        action_name_tc = converter.convert(action_name_sc)
        action_map[action_id] = action_name_tc

    print(f"Processed {len(action_map)} actions.")

    # Write to JSON
    with open(OUTPUT_FILENAME, 'w', encoding='utf-8') as f:
        json.dump(action_map, f, ensure_ascii=False, separators=(',', ':'))

    print(f"Successfully generated {OUTPUT_FILENAME}")


if __name__ == "__main__":
    main()
