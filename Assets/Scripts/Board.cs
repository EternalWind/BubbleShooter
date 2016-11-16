using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class Board : MonoBehaviour
{
    public class BubbleSlot
    {
        public Bubble bubble;
    }

    public class BubbleCollision
    {
        public Vector2 grid;
        public bool isSticking;
        public Vector2 collidingPointNormal;
    }

    public int bubblesPerRow = 9;
    public int boardHeightInBubbleRows = 13;
    public float bubbleRadius = 0.5f;
    public float bubbleShootingSpeed = 2f;

    public GameObject canon;

    public Bubble bubblePrefab;

    private BubbleSlot[] slots;
    private float hexagonSize;
    private float leftBoarder = 0.0f;
    private float rightBoarder;
    private float topBoarder = 0.0f;
    private float bottomBoarder;

    private float collideThreshold = 0.95f;

    private bool canShoot = true;
    private Bubble nextBubble;
    private Bubble currentBubble;

    private Vector2[] neighbourGridOffsetsForEvenRow
        = { new Vector2(-1, 0), new Vector2(-1, -1), new Vector2(0, -1), new Vector2(1, 0), new Vector2(0, 1), new Vector2(-1, 1) };
    private Vector2[] neighbourGridOffsetsForOddRow
        = { new Vector2(-1, 0), new Vector2(0, -1), new Vector2(1, -1), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };

    // Use this for initialization
    void Start()
    {
        slots = new BubbleSlot[bubblesPerRow * boardHeightInBubbleRows];

        hexagonSize = bubbleRadius / Mathf.Cos(Mathf.Deg2Rad * 30.0f);
        var hexagonHeight = hexagonSize * 2;
        var hexagonVerticalDistance = hexagonHeight * 3 / 4;

        rightBoarder = leftBoarder + bubbleRadius * bubblesPerRow * 2 + bubbleRadius;
        bottomBoarder = topBoarder - (boardHeightInBubbleRows - 1) * hexagonVerticalDistance - hexagonSize * 2;

        ReloadCanon();
    }

    private Bubble GenerateRandomBubble()
    {
        return Instantiate(bubblePrefab);
    }

    private void Shoot(Vector2 dir, Bubble bubble)
    {
        currentBubble = bubble;
        StartCoroutine(ShootImpl(dir, bubble));
    }

    private Vector3 Hex2Cube(Vector2 hex_coordinate)
    {
        return new Vector3(hex_coordinate.x, -hex_coordinate.x - hex_coordinate.y, hex_coordinate.y);
    }

    private Vector2 Cube2Hex(Vector3 cube_coordinate)
    {
        return new Vector2(cube_coordinate.x, cube_coordinate.z);
    }

    private Vector3 CubeRound(Vector3 cube_coordinate)
    {
        var rx = Mathf.Round(cube_coordinate.x);
        var ry = Mathf.Round(cube_coordinate.y);
        var rz = Mathf.Round(cube_coordinate.z);

        var x_diff = Mathf.Abs(rx - cube_coordinate.x);
        var y_diff = Mathf.Abs(ry - cube_coordinate.y);
        var z_diff = Mathf.Abs(rz - cube_coordinate.z);

        if (x_diff > y_diff && x_diff > z_diff)
        {
            rx = -ry - rz;
        }
        else if (y_diff > z_diff)
        {
            ry = -rx - rz;
        }
        else
        {
            rz = -rx - ry;
        }

        return new Vector3(rx, ry, rz);
    }

    private Vector2 HexRound(Vector2 hex_coordinate)
    {
        return Cube2Hex(CubeRound(Hex2Cube(hex_coordinate)));
    }

    private Vector3 Local2Cube(Vector2 local_coordinate)
    {
        local_coordinate.x = local_coordinate.x - bubbleRadius;
        local_coordinate.y = -local_coordinate.y - hexagonSize;

        var q = (local_coordinate.x * Mathf.Sqrt(3) / 3 - local_coordinate.y / 3) / hexagonSize;
        var r = local_coordinate.y * 2 / 3 / hexagonSize;

        return CubeRound(Hex2Cube(new Vector2(q, r)));
    }

    private Vector2 Cube2Grid(Vector3 cube_coordinate)
    {
        return new Vector2(cube_coordinate.x + (cube_coordinate.z - ((int)cube_coordinate.z & 1)) / 2,
            cube_coordinate.z);
    }

    private Vector2 Local2Grid(Vector2 local_coordinate)
    {
        return Cube2Grid(Local2Cube(local_coordinate));
    }

    private Vector2 Grid2Local(Vector2 grid_coordinate)
    {
        var x = hexagonSize * Mathf.Sqrt(3) * (grid_coordinate.x + 0.5 * ((int)grid_coordinate.y & 1));
        var y = hexagonSize * 3 / 2 * grid_coordinate.y;

        return new Vector2((float)x + bubbleRadius, -y - hexagonSize);
    }

    private int Grid2Index(Vector2 grid_coordinate)
    {
        return (int)(grid_coordinate.x + grid_coordinate.y * bubblesPerRow);
    }

    private bool IsGridCoordValid(Vector2 grid_coordinate)
    {
        return grid_coordinate.x >= 0 && grid_coordinate.x < bubblesPerRow
            && grid_coordinate.y >= 0 && grid_coordinate.y < boardHeightInBubbleRows;
    }

    private bool CollisionTest(Bubble bubble, out BubbleCollision collision)
    {
        var grid_coordinate = Local2Grid(bubble.transform.localPosition);
        collision = null;

        if (IsGridCoordValid(grid_coordinate))
        {
            Vector2[] neighbourGridOffsets = ((int)grid_coordinate.y & 1) == 0 ?
                neighbourGridOffsetsForEvenRow : neighbourGridOffsetsForOddRow;

            var potential_collisions = new List<KeyValuePair<Vector2, float>>();

            foreach (var offset in neighbourGridOffsets)
            {
                var neighbour_grid_coord = grid_coordinate + offset;
                var neighbour_local_coord = Grid2Local(neighbour_grid_coord);
                var distance_between_bubble_and_neighbour = Vector2.Distance(bubble.transform.localPosition, neighbour_local_coord);

                if (distance_between_bubble_and_neighbour <= collideThreshold * bubbleRadius * 2
                    && neighbour_grid_coord.y < boardHeightInBubbleRows /* Nothing would happen even if the bubble collides with the bottom wall */)
                {
                    potential_collisions.Add(new KeyValuePair<Vector2, float>(neighbour_grid_coord,
                        distance_between_bubble_and_neighbour));
                }
            }

            var prioritized_potential_colliders_grid = potential_collisions.OrderBy(t => t.Value).Select(t => t.Key);

            foreach (var potential_collider_grid in prioritized_potential_colliders_grid)
            {
                var is_colliding_with_upper_wall = potential_collider_grid.y < 0.0f;
                var is_colliding_with_left_wall = potential_collider_grid.x < 0.0f;
                var is_colliding_with_right_wall = potential_collider_grid.x >= bubblesPerRow;
                var is_colliding_with_side_walls = is_colliding_with_left_wall || is_colliding_with_right_wall;

                if (is_colliding_with_side_walls)
                {
                    // Colliding with a wall...
                    collision = new BubbleCollision
                    {
                        isSticking = false,
                        grid = grid_coordinate,
                        collidingPointNormal = is_colliding_with_left_wall ? Vector2.right : Vector2.left
                    };

                    return true;
                }
                else if (is_colliding_with_upper_wall || slots[Grid2Index(potential_collider_grid)] != null)
                {
                    // Colliding with another bubble...
                    collision = new BubbleCollision
                    {
                        isSticking = true,
                        grid = grid_coordinate,
                        collidingPointNormal = Vector3.zero
                    };

                    return true;
                }
            }
        }

        return false;
    }

    public void OnDrawGizmos()
    {
        Gizmos.color = Color.green;

        for (int i = 0; i < bubblesPerRow; ++i)
        {
            for (int j = 0; j < boardHeightInBubbleRows; ++j)
            {
                var local = Grid2Local(new Vector2(i, j));
                Gizmos.DrawWireSphere(local + (Vector2)transform.position, bubbleRadius);
            }
        }

        if (currentBubble != null)
        {
            var grid = Local2Grid(currentBubble.transform.localPosition);
            var normalized_local_for_current_bubble = Grid2Local(grid);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(normalized_local_for_current_bubble + (Vector2)transform.position, bubbleRadius);
        }
    }

    private IEnumerator ShootImpl(Vector2 dir, Bubble bubble)
    {
        canShoot = false;

        while (true)
        {
            bubble.transform.Translate(dir * bubbleShootingSpeed * Time.deltaTime);

            BubbleCollision collision = null;
            if (CollisionTest(bubble, out collision))
            {
                if (collision.isSticking)
                {
                    bubble.transform.localPosition = Grid2Local(collision.grid);
                    slots[Grid2Index(collision.grid)] = new BubbleSlot { bubble = bubble };
                }
                else
                {
                    yield return StartCoroutine(ShootImpl(Vector2.Reflect(dir, collision.collidingPointNormal), bubble));
                }

                break;
            }

            yield return null;
        }

        canShoot = true;

        yield break;
    }

    private void ReloadCanon()
    {
        nextBubble = GenerateRandomBubble();
        nextBubble.transform.position = canon.transform.position;
        nextBubble.transform.SetParent(this.transform);
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (canShoot)
            {
                var click_position = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                var canon_position = canon.transform.position;

                var fire_direction = click_position - canon_position;
                fire_direction = (fire_direction != Vector3.zero) ? fire_direction.normalized : Vector3.up;

                Shoot(fire_direction, nextBubble);
                ReloadCanon();
            }
        }
    }
}
