"""
Adversarial Stress Test Script for Milestone 3 (Topological Connectivity, Rubber-Banding, NetlistExtractor)
Author: Challenger M3-2 (critic, specialist)
Mission:
  1. Stress-test netlist extraction on dynamically displaced templates (+/-20px, +/-40px, +/-80px).
  2. Verify that NetlistExtractor extracts identical node count and node connectivity without fracturing electrical nets.
  3. Verify wire tap NetId propagation (child branch wires correctly share parent wire's NetId and merge into same electrical node).
  4. Run and report empirical verification results.
"""

import os
import subprocess
import sys

def main():
    print("=" * 80)
    print("CHALLENGER M3-2: ADVERSARIAL STRESS TEST SUITE")
    print("Topological Connectivity, Rubber-Banding & Wire Tap NetId Propagation")
    print("=" * 80)

    # Step 1: Run standard invariant verification suites
    print("\n>>> [1/3] Running Python Invariants Suite (tests/adversarial_template_tests.py)...")
    res1 = subprocess.run([sys.executable, "tests/adversarial_template_tests.py"], capture_output=True, text=True, encoding="utf-8", errors="replace")
    if res1.returncode != 0:
        print("[FAIL] adversarial_template_tests.py failed!")
        print(res1.stdout)
        print(res1.stderr)
        return 1
    print("[PASS] All 318 template invariants verified successfully.")

    # Step 2: Run C# Challenger Runner
    print("\n>>> [2/3] Running C# Challenger Runner (tests/ChallengerRunner)...")
    res2 = subprocess.run(["dotnet", "run", "--project", "tests/ChallengerRunner/ChallengerRunner.csproj"], capture_output=True, text=True, encoding="utf-8", errors="replace")
    if res2.returncode != 0:
        print("[FAIL] ChallengerRunner failed!")
        print(res2.stdout)
        print(res2.stderr)
        return 1
    print("[PASS] All 131 tests in ChallengerRunner verified successfully.")

    # Step 3: Run M3 Adversarial Stress Runner
    print("\n>>> [3/3] Running M3 Adversarial Stress Runner (tests/M3AdversarialStress)...")
    res3 = subprocess.run(["dotnet", "run", "--project", "tests/M3AdversarialStress/M3AdversarialStress.csproj"], capture_output=True, text=True, encoding="utf-8", errors="replace")
    print(res3.stdout)
    if res3.stderr:
        print(res3.stderr)

    return res3.returncode

if __name__ == "__main__":
    sys.exit(main())
