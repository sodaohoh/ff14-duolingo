import requests
import csv
import json
import os
import opencc

# Config
# Source URL for Chinese Data (Maintained by community for CN server)
# If this link fails, you might need to find the latest CN datamining repo.
CN_ACTION_CSV_URL = "https://raw.githubusercontent.com/thewakingsands/ffxiv-datamining-cn/refs/heads/master/Action.csv"
OUTPUT_FILENAME = "actions_zhtw.json"

def main():
    print(f"Downloading CSV from {CN_ACTION_CSV_URL}...")

    try:
        response = requests.get(CN_ACTION_CSV_URL)
        response.raise_for_status()
    except Exception as e:
        print(f"Failed to download: {e}")
        return

    # Decode content (CN data usually UTF-8)
    decoded_content = response.content.decode('utf-8')
    csv_reader = csv.reader(decoded_content.splitlines())

    # Initialize OpenCC for Simplified to Traditional (Taiwan)
    # s2twp: Simplified Chinese to Traditional Chinese (Taiwan Phrase)
    converter = opencc.OpenCC('s2twp.json')

    action_map = {}

    # Skip header rows (FFXIV CSVs usually have 3 header rows)
    # Row 0: Keys (Index, Name, ...)
    # Row 1: Types (int, str, ...)
    # Row 2: Default Values
    headers_skipped = False

    row_count = 0

    for row in csv_reader:
        if len(row) < 2:
            continue

        # Basic heuristic to skip headers: check if the first column is a number
        if not row[0].isdigit():
            continue

        action_id = row[0]
        action_name_sc = row[1] # Column 1 is usually the Name

        # Skip empty names or system placeholders
        if not action_name_sc:
            continue

        # Convert to Traditional Chinese
        action_name_tc = converter.convert(action_name_sc)

        # Store in map
        action_map[action_id] = action_name_tc
        row_count += 1

    print(f"Processed {row_count} actions.")

    # Save to JSON
    with open(OUTPUT_FILENAME, 'w', encoding='utf-8') as f:
        json.dump(action_map, f, ensure_ascii=False, indent=None)

    print(f"Successfully generated {OUTPUT_FILENAME}")
    print("Please copy this file to your Dalamud plugin folder.")

if __name__ == "__main__":
    main()
