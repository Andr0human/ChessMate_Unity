import csv
import os
import matplotlib.pyplot as plt

CSV_PATH = os.path.join(os.path.dirname(__file__), '..', 'arena', 'results_log.csv')


def display_results(wins, draws, losses, engine1, engine2):
    total_matches = wins + draws + losses
    if total_matches == 0:
        return

    win_pct  = round((wins   / total_matches) * 100, 2)
    draw_pct = round((draws  / total_matches) * 100, 2)
    loss_pct = round((losses / total_matches) * 100, 2)

    fig, ax = plt.subplots(figsize=(10, 1.6))

    categories = ['']

    bars_wins   = ax.barh(categories, wins,   color='green', label=f'Wins ({wins})')
    bars_draws  = ax.barh(categories, draws,  left=wins,         color='grey', label=f'Draws ({draws})')
    bars_losses = ax.barh(categories, losses, left=wins + draws, color='red',  label=f'Losses ({losses})')

    for bar, value in zip(bars_wins, [win_pct]):
        if value > 0:
            ax.text(bar.get_x() + bar.get_width() / 2, bar.get_y() + bar.get_height() / 2,
                    f'{value}%', ha='center', va='center', color='white', fontweight='bold')
    for bar, value in zip(bars_draws, [draw_pct]):
        if value > 0:
            ax.text(bar.get_x() + bar.get_width() / 2, bar.get_y() + bar.get_height() / 2,
                    f'{value}%', ha='center', va='center', color='white', fontweight='bold')
    for bar, value in zip(bars_losses, [loss_pct]):
        if value > 0:
            ax.text(bar.get_x() + bar.get_width() / 2, bar.get_y() + bar.get_height() / 2,
                    f'{value}%', ha='center', va='center', color='white', fontweight='bold')

    score = wins + 0.5 * draws
    ax.set_title(
        f'{engine1} vs {engine2}   |   '
        f'Score: {score:g} / {total_matches}   ({wins}W / {draws}D / {losses}L)',
        fontsize=11, fontweight='bold'
    )

    ax.set_xlim(0, total_matches)
    ax.set_xticks([])
    ax.set_yticks([])
    ax.legend(loc='lower right', bbox_to_anchor=(1.0, -0.55), ncol=3, frameon=False)

    plt.tight_layout()
    plt.savefig("results.png", bbox_inches='tight')
    plt.close()


def display_score_graph(rows, engine1, engine2):
    e1_score = 0
    e2_score = 0
    e1_scores = []
    e2_scores = []

    for row in rows:
        result = row['Result']
        white  = row['WhitePlayer']

        if result == '1/2-1/2':
            e1_score += 0.5
            e2_score += 0.5
        elif result == '1-0':
            if white == engine1: e1_score += 1
            else:                e2_score += 1
        else:  # 0-1
            if white == engine1: e2_score += 1
            else:                e1_score += 1

        e1_scores.append(e1_score)
        e2_scores.append(e2_score)

    n = len(rows)
    games = list(range(1, n + 1))
    final_e1 = e1_scores[-1]
    final_e2 = e2_scores[-1]

    use_markers = n <= 50
    marker = 'o' if use_markers else None

    plt.figure(figsize=(10, 6))
    plt.plot(games, e1_scores, marker=marker, label=f'{engine1} ({final_e1:g})')
    plt.plot(games, e2_scores, marker=marker, label=f'{engine2} ({final_e2:g})')
    plt.xlabel('Game')
    plt.ylabel('Score (win=1, draw=0.5)')
    plt.title(f'Cumulative Score: {engine1} vs {engine2}')
    plt.legend()
    plt.grid(True)
    plt.xlim(1, n)
    if n <= 30:
        plt.xticks(games)
    plt.savefig("scores.png", bbox_inches='tight')
    plt.close()


def read_csv(file_path):
    with open(file_path, 'r') as f:
        reader = csv.DictReader(f, skipinitialspace=True)
        rows = [row for row in reader]
    return rows


def tally(rows, engine1):
    wins = draws = losses = 0
    for row in rows:
        result = row['Result']
        white  = row['WhitePlayer']

        if result == '1/2-1/2':
            draws += 1
        elif result == '1-0':
            if white == engine1: wins += 1
            else:                losses += 1
        else:  # 0-1
            if white == engine1: losses += 1
            else:                wins += 1
    return wins, draws, losses


rows = read_csv(CSV_PATH)
if rows:
    engine1 = rows[0]['WhitePlayer']
    engine2 = rows[0]['BlackPlayer']
    wins, draws, losses = tally(rows, engine1)

    display_results(wins, draws, losses, engine1, engine2)
    display_score_graph(rows, engine1, engine2)
